using System.Net.Security;
using System.Security.Authentication;
using MassTransit;
using MassTransit.Logging;
using MassTransit.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microservices.Messaging;

public static class RabbitMqMessagingExtensions
{
    /// <summary>
    /// Registers MassTransit using RabbitMQ with PostgreSQL-backed Entity Framework Core
    /// bus and consumer outboxes.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The service-owned context used by the bus outbox and every automatically configured
    /// receive endpoint. Its model must include the mappings added by
    /// <see cref="AddMassTransitOutboxEntities"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration containing either <c>ConnectionStrings:rabbitmq</c> or the
    /// <c>Messaging</c> fallback settings.
    /// </param>
    /// <param name="endpointNamePrefix">
    /// A stable lowercase kebab-case topology identifier for the service. Every replica must
    /// use the same value. Do not include a machine, pod, process, deployment slot, or random
    /// identifier. Changing this value changes receive queue names and is a topology migration.
    /// The value is also used as the service-level RabbitMQ client connection label.
    /// </param>
    /// <param name="configureRegistrations">
    /// Optionally registers MassTransit consumers, sagas, activities, and their definitions.
    /// </param>
    /// <param name="configureAutomaticEndpoint">
    /// Optionally configures each registration-driven receive endpoint before the required
    /// Entity Framework Core inbox/outbox middleware is attached.
    /// </param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// This helper supports receive endpoints created from MassTransit registrations by
    /// <c>ConfigureEndpoints</c>. Raw <c>ReceiveEndpoint</c> declarations are intentionally not
    /// exposed because they bypass the shared endpoint callback and its consumer inbox/outbox.
    /// <typeparamref name="TDbContext"/> is deliberately the single transactional context for
    /// this service. Business changes that must be atomic with message consumption must be saved
    /// through that same scoped context; another context or database is outside the transaction.
    /// Outside a consumer, publish or send through the scoped <see cref="IPublishEndpoint"/> or
    /// <see cref="ISendEndpointProvider"/> before saving <typeparamref name="TDbContext"/>.
    /// </remarks>
    public static IServiceCollection AddRabbitMqWithPostgresOutbox<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string endpointNamePrefix,
        Action<IBusRegistrationConfigurator>? configureRegistrations = null,
        ConfigureEndpointsProviderCallback? configureAutomaticEndpoint = null)
        where TDbContext : DbContext
    {
        ValidateEndpointNamePrefix(endpointNamePrefix);

        var options = configuration.GetSection(RabbitMqMessagingOptions.SectionName)
            .Get<RabbitMqMessagingOptions>() ?? new RabbitMqMessagingOptions();
        var rabbitConnectionString = configuration.GetConnectionString(
            RabbitMqMessagingOptions.ConnectionStringName);
        var rabbitHostAddress = RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
            options,
            rabbitConnectionString);

        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddSource(DiagnosticHeaders.DefaultListenerName));
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.AddMeter(InstrumentationOptions.MeterName));

        services.AddMassTransit(registration =>
        {
            registration.SetEndpointNameFormatter(
                new KebabCaseEndpointNameFormatter(endpointNamePrefix, false));
            configureRegistrations?.Invoke(registration);

            registration.AddEntityFrameworkOutbox<TDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.QueryDelay = options.OutboxQueryDelay;
                outbox.DuplicateDetectionWindow = options.DuplicateDetectionWindow;
            });

            registration.AddConfigureEndpointsCallback((context, name, endpoint) =>
            {
                configureAutomaticEndpoint?.Invoke(context, name, endpoint);
                endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
            });

            registration.UsingRabbitMq((context, rabbit) =>
            {
                if (rabbitHostAddress is not null)
                {
                    rabbit.Host(rabbitHostAddress, endpointNamePrefix, host =>
                    {
                        if (RabbitMqMessagingOptionsValidator.IsSecureScheme(
                                rabbitHostAddress.Scheme))
                        {
                            ConfigureTls(host, options, rabbitHostAddress.Host);
                        }
                    });
                }
                else
                {
                    var port = options.Port ?? (options.UseTls ? (ushort)5671 : (ushort)5672);
                    rabbit.Host(
                        options.Host,
                        port,
                        options.VirtualHost,
                        endpointNamePrefix,
                        host =>
                        {
                            host.Username(options.Username);
                            host.Password(options.Password);

                            if (options.UseTls)
                            {
                                ConfigureTls(host, options, options.Host);
                            }
                        });
                }

                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static void ConfigureTls(
        IRabbitMqHostConfigurator host,
        RabbitMqMessagingOptions options,
        string defaultServerName)
    {
        host.UseSsl(ssl => ConfigureTls(ssl, options, defaultServerName));
    }

    internal static void ConfigureTls(
        IRabbitMqSslConfigurator ssl,
        RabbitMqMessagingOptions options,
        string defaultServerName)
    {
        // None delegates protocol selection to the operating-system security policy.
        ssl.Protocol = SslProtocols.None;
        ssl.ServerName = options.TlsServerName ?? defaultServerName;
        ssl.EnforcePolicyErrors(
            SslPolicyErrors.RemoteCertificateChainErrors |
            SslPolicyErrors.RemoteCertificateNameMismatch |
            SslPolicyErrors.RemoteCertificateNotAvailable);
    }

    internal static void ValidateEndpointNamePrefix(string endpointNamePrefix)
    {
        if (string.IsNullOrWhiteSpace(endpointNamePrefix) ||
            endpointNamePrefix.Length > 64 ||
            endpointNamePrefix[0] == '-' ||
            endpointNamePrefix[^1] == '-' ||
            endpointNamePrefix.Contains("--", StringComparison.Ordinal) ||
            endpointNamePrefix.Any(character =>
                character != '-' &&
                !char.IsAsciiLetterLower(character) &&
                !char.IsAsciiDigit(character)))
        {
            throw new ArgumentException(
                "Endpoint name prefix must be 1-64 characters of lowercase kebab-case text.",
                nameof(endpointNamePrefix));
        }
    }

    /// <summary>
    /// Adds MassTransit's inbox and outbox entity mappings to the Entity Framework Core model.
    /// </summary>
    /// <remarks>
    /// Call this from <c>OnModelCreating</c> on the same context passed to
    /// <see cref="AddRabbitMqWithPostgresOutbox{TDbContext}"/>, then create and deploy a migration
    /// for the three infrastructure tables.
    /// </remarks>
    public static void AddMassTransitOutboxEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
