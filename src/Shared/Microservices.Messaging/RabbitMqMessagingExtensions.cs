using System.Net.Security;
using System.Security.Authentication;
using MassTransit;
using MassTransit.Logging;
using MassTransit.Monitoring;
using Microservices.Application.Messaging;
using Microservices.Contracts;
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
    /// bus and consumer outboxes, bounded transient retry, and bounded delayed redelivery.
    /// </summary>
    public static IServiceCollection AddRabbitMqWithPostgresOutbox<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string endpointNamePrefix,
        Action<IBusRegistrationConfigurator>? configureRegistrations = null,
        ConfigureEndpointsProviderCallback? configureAutomaticEndpoint = null)
        where TDbContext : DbContext
    {
        ValidateEndpointNamePrefix(endpointNamePrefix);
        RabbitMqMessagingOptionsValidator.RejectRemovedConfiguration(configuration);

        var options = configuration.GetSection(RabbitMqMessagingOptions.SectionName)
            .Get<RabbitMqMessagingOptions>() ?? new RabbitMqMessagingOptions();
        var rabbitConnectionString = configuration.GetConnectionString(
            RabbitMqMessagingOptions.ConnectionStringName);
        var rabbitHostAddress = RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
            options,
            rabbitConnectionString);
        var policyRegistry = new ConsumerPolicyRegistry(options);

        services.AddSingleton(options);
        services.AddSingleton<IConsumerExceptionClassifier, ConsumerExceptionClassifier>();
        services.AddSingleton<IntegrationConsumeContextAccessor>();
        services.AddScoped<IIntegrationMessagePublisher, MassTransitIntegrationMessagePublisher>();
        services.AddSingleton<OutboxMetricsCollector<TDbContext>>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<OutboxMetricsCollector<TDbContext>>());
        services.AddHealthChecks().AddCheck<OutboxCollectorHealthCheck<TDbContext>>(
            "messaging-outbox-collector",
            tags: ["ready"]);
        services.Configure<MassTransitHostOptions>(host =>
        {
            host.WaitUntilStarted = true;
            host.StartTimeout = options.StartTimeout;
            host.StopTimeout = options.StopTimeout;
            host.ConsumerStopTimeout = options.ConsumerStopTimeout;
        });

        services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddSource(DiagnosticHeaders.DefaultListenerName));
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            metrics.AddMeter(InstrumentationOptions.MeterName);
            metrics.AddMeter(MessagingInstrumentation.MeterName);
        });

        services.AddMassTransit(registration =>
        {
            registration.SetEndpointNameFormatter(
                new KebabCaseEndpointNameFormatter(endpointNamePrefix, false));
            ConsumerPolicyRegistryStore.Attach(registration, policyRegistry);
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

                var configuredPolicy = policyRegistry.Resolve(name);
                var policy = ResolvePolicy(options, configuredPolicy);
                var classifier = context.GetRequiredService<IConsumerExceptionClassifier>();

                endpoint.PrefetchCount = policy.PrefetchCount;
                endpoint.ConcurrentMessageLimit = policy.ConcurrentMessageLimit;

                if (endpoint is IRabbitMqReceiveEndpointConfigurator rabbitEndpoint)
                {
                    ConfigureReceiveQueue(rabbitEndpoint, options, policy);
                }

                if (policy.RateLimit is not null)
                {
                    endpoint.UseRateLimit(
                        policy.RateLimit.Value,
                        policy.RateLimitInterval!.Value);
                }

                // Registration order is outer-to-inner. Delayed redelivery wraps immediate retry;
                // the attempt filter is inside retry and therefore observes every consumer invocation.
                endpoint.UseDelayedRedelivery(redelivery =>
                {
                    redelivery.Intervals(policy.RedeliveryIntervals);
                    redelivery.Handle<Exception>(classifier.IsTransient);
                });

                endpoint.UseMessageRetry(retry =>
                {
                    retry.Intervals(policy.RetryIntervals);
                    retry.Handle<Exception>(classifier.IsTransient);
                });

                endpoint.UseConsumeFilter(typeof(ConsumerDeliveryMetricsFilter<>), context);
                endpoint.UseConsumeFilter(typeof(IntegrationConsumeContextFilter<>), context);
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

                rabbit.ConfigureJsonSerializerOptions(IntegrationContractJson.Configure);
                rabbit.SendTopology.ConfigureErrorSettings = settings =>
                    ConfigureFaultQueue(settings, options);
                rabbit.SendTopology.ConfigureDeadLetterSettings = settings =>
                    ConfigureFaultQueue(settings, options);

                // RabbitMQ delayed-message exchange is a required production capability. Missing
                // plugin support is a deployment failure and must not silently change semantics.
                rabbit.UseDelayedMessageScheduler();
                rabbit.ConfigureEndpoints(context);
                policyRegistry.ValidateAllPoliciesMatched();
            });
        });

        return services;
    }

    private static void ConfigureReceiveQueue(
        IRabbitMqReceiveEndpointConfigurator endpoint,
        RabbitMqMessagingOptions options,
        ResolvedConsumerDeliveryPolicy policy)
    {
        endpoint.Durable = true;
        endpoint.AutoDelete = false;
        endpoint.SingleActiveConsumer = policy.SingleActiveConsumer;

        if (options.UseQuorumQueues)
        {
            endpoint.SetQuorumQueue();
            endpoint.SetQueueArgument("x-delivery-limit", options.QueueDeliveryLimit);
        }

        // Durable business receive queues deliberately have no x-message-ttl. Expiration belongs
        // on explicitly published messages or on a separately governed parking topology.
        endpoint.SetQueueArgument("x-max-length", options.QueueMaxLength);
        endpoint.SetQueueArgument("x-max-length-bytes", options.QueueMaxLengthBytes);
        endpoint.SetQueueArgument("x-overflow", "reject-publish");
    }

    private static void ConfigureFaultQueue(
        IRabbitMqQueueBindingConfigurator settings,
        RabbitMqMessagingOptions options)
    {
        settings.Durable = true;
        settings.AutoDelete = false;

        if (options.UseQuorumQueues)
        {
            settings.SetQuorumQueue();
            settings.SetQueueArgument("x-delivery-limit", -1);
        }

        settings.SetQueueArgument("x-message-ttl", options.FaultQueueRetention);
        settings.SetQueueArgument("x-max-length", options.FaultQueueMaxLength);
    }

    private static ResolvedConsumerDeliveryPolicy ResolvePolicy(
        RabbitMqMessagingOptions options,
        ConsumerDeliveryPolicyOptions consumer) =>
        new(
            consumer.RetryIntervals ?? options.RetryIntervals,
            consumer.RedeliveryIntervals ?? options.RedeliveryIntervals,
            consumer.PrefetchCount ?? options.PrefetchCount,
            consumer.ConcurrentMessageLimit ?? options.ConcurrentMessageLimit,
            consumer.RateLimit,
            consumer.RateLimitInterval,
            consumer.SingleActiveConsumer);

    private sealed record ResolvedConsumerDeliveryPolicy(
        TimeSpan[] RetryIntervals,
        TimeSpan[] RedeliveryIntervals,
        ushort PrefetchCount,
        ushort ConcurrentMessageLimit,
        int? RateLimit,
        TimeSpan? RateLimitInterval,
        bool SingleActiveConsumer);

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

    public static void AddMassTransitOutboxEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
