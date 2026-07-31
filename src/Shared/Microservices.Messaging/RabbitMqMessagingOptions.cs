namespace Microservices.Messaging;

/// <summary>
/// Configures the RabbitMQ transport and MassTransit PostgreSQL outbox registered by
/// <see cref="RabbitMqMessagingExtensions.AddRabbitMqWithPostgresOutbox{TDbContext}"/>.
/// </summary>
public sealed class RabbitMqMessagingOptions
{
    public const string SectionName = "Messaging";
    public const string ConnectionStringName = "rabbitmq";

    public string Host { get; init; } = string.Empty;
    public string VirtualHost { get; init; } = "/";
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public ushort? Port { get; init; }
    public bool UseTls { get; init; } = true;
    public string? TlsServerName { get; init; }
    public TimeSpan OutboxQueryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan DuplicateDetectionWindow { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Short, in-memory retry intervals. Keep these bounded and brief.</summary>
    public TimeSpan[] RetryIntervals { get; init; } =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3)
    ];

    /// <summary>
    /// Broker-backed delayed redelivery intervals after immediate retries are exhausted.
    /// RabbitMQ's delayed-message exchange plugin is required.
    /// </summary>
    public TimeSpan[] RedeliveryIntervals { get; init; } =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5)
    ];

    public ushort PrefetchCount { get; init; } = 32;
    public ushort ConcurrentMessageLimit { get; init; } = 8;
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConsumerStopTimeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Optional endpoint-name keyed overrides. Keys are the final stable queue names produced by
    /// the configured endpoint name formatter, for example service-template-submit-order.
    /// </summary>
    public Dictionary<string, ConsumerDeliveryPolicyOptions> Consumers { get; init; } =
        new(StringComparer.Ordinal);
}

public sealed class ConsumerDeliveryPolicyOptions
{
    public TimeSpan[]? RetryIntervals { get; init; }
    public TimeSpan[]? RedeliveryIntervals { get; init; }
    public ushort? PrefetchCount { get; init; }
    public ushort? ConcurrentMessageLimit { get; init; }
}
