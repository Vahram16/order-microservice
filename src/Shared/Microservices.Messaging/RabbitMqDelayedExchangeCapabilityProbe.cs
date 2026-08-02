using System.Net.Security;
using System.Security.Authentication;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Microservices.Messaging;

/// <summary>
/// Proves the broker can declare the exchange type required by delayed redelivery. Configuring
/// MassTransit's scheduler alone is lazy and does not validate plugin availability at bus startup.
/// </summary>
internal sealed class RabbitMqDelayedExchangeCapabilityProbe(
    RabbitMqMessagingOptions options,
    Uri? configuredHostAddress,
    string endpointNamePrefix) : IHostedService
{
    private const string DelayedExchangeType = "x-delayed-message";
    private const string DelayedUnderlyingTypeArgument = "x-delayed-type";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartTimeout);

        var exchangeName = $"{endpointNamePrefix}-delayed-capability-{Guid.NewGuid():N}";
        var factory = CreateConnectionFactory(options, configuredHostAddress, endpointNamePrefix);

        try
        {
            await using var connection = await factory.CreateConnectionAsync(
                $"{endpointNamePrefix}:delayed-exchange-capability",
                timeout.Token).ConfigureAwait(false);
            await using var channel = await connection.CreateChannelAsync(
                cancellationToken: timeout.Token).ConfigureAwait(false);

            await channel.ExchangeDeclareAsync(
                exchangeName,
                DelayedExchangeType,
                durable: false,
                autoDelete: true,
                arguments: new Dictionary<string, object?>
                {
                    [DelayedUnderlyingTypeArgument] = ExchangeType.Fanout
                },
                cancellationToken: timeout.Token).ConfigureAwait(false);
            await channel.ExchangeDeleteAsync(
                exchangeName,
                cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "RabbitMQ does not provide the x-delayed-message exchange capability required " +
                "by the configured delayed-redelivery policy. Install and enable the compatible " +
                "rabbitmq_delayed_message_exchange plugin before starting the service.",
                exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ConnectionFactory CreateConnectionFactory(
        RabbitMqMessagingOptions options,
        Uri? configuredHostAddress,
        string endpointNamePrefix)
    {
        var factory = new ConnectionFactory
        {
            AutomaticRecoveryEnabled = false,
            ClientProvidedName = $"{endpointNamePrefix}:delayed-exchange-capability"
        };

        if (configuredHostAddress is not null)
        {
            var normalizedAddress = new UriBuilder(configuredHostAddress)
            {
                Scheme = RabbitMqMessagingOptionsValidator.IsSecureScheme(configuredHostAddress.Scheme)
                    ? "amqps"
                    : "amqp"
            }.Uri;
            factory.Uri = normalizedAddress;
            ConfigureTls(
                factory,
                RabbitMqMessagingOptionsValidator.IsSecureScheme(configuredHostAddress.Scheme),
                options.TlsServerName ?? configuredHostAddress.Host);
            return factory;
        }

        factory.HostName = options.Host;
        factory.Port = options.Port ?? (options.UseTls ? 5671 : 5672);
        factory.VirtualHost = options.VirtualHost;
        factory.UserName = options.Username;
        factory.Password = options.Password;
        ConfigureTls(factory, options.UseTls, options.TlsServerName ?? options.Host);
        return factory;
    }

    private static void ConfigureTls(
        ConnectionFactory factory,
        bool enabled,
        string serverName)
    {
        if (!enabled)
        {
            return;
        }

        factory.Ssl = new SslOption
        {
            Enabled = true,
            ServerName = serverName,
            Version = SslProtocols.None,
            AcceptablePolicyErrors = SslPolicyErrors.None
        };
    }
}
