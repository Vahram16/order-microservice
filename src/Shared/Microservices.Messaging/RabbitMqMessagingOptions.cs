namespace Microservices.Messaging;

/// <summary>
/// Configures the RabbitMQ transport and MassTransit PostgreSQL outbox registered by
/// <see cref="RabbitMqMessagingExtensions.AddRabbitMqWithPostgresOutbox{TDbContext}"/>.
/// </summary>
public sealed class RabbitMqMessagingOptions
{
    public const string SectionName = "Messaging";
    public const string ConnectionStringName = "rabbitmq";
    internal const string RemovedReceiveQueueTtlSetting = "QueueMessageTimeToLive";

    public string Host { get; init; } = string.Empty;
    public string VirtualHost { get; init; } = "/";
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public ushort? Port { get; init; }
    public bool UseTls { get; init; } = true;
    public string? TlsServerName { get; init; }
    public TimeSpan OutboxQueryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan DuplicateDetectionWindow { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan OutboxMetricsInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan OutboxMetricsQueryTimeout { get; init; } = TimeSpan.FromSeconds(5);

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

    /// <summary>
    /// Maximum aggregate wait introduced by retry and delayed-redelivery middleware. Consumer
    /// execution time is not included and must be bounded by the owning application where needed.
    /// </summary>
    public TimeSpan MaximumRetryAndRedeliveryDelay { get; init; } = TimeSpan.FromMinutes(30);

    public ushort PrefetchCount { get; init; } = 32;
    public ushort ConcurrentMessageLimit { get; init; } = 8;
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConsumerStopTimeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>Creates durable quorum receive queues for production-safe replication.</summary>
    public bool UseQuorumQueues { get; init; } = true;

    /// <summary>Maximum number of ready messages retained by each receive queue.</summary>
    public long QueueMaxLength { get; init; } = 100_000;

    /// <summary>Maximum aggregate ready-message bytes retained by each receive queue.</summary>
    public long QueueMaxLengthBytes { get; init; } = 1_073_741_824;

    /// <summary>
    /// Broker delivery limit used as a final guard against requeue loops outside MassTransit.
    /// MassTransit retry and redelivery remain independently bounded by their interval arrays.
    /// </summary>
    public int QueueDeliveryLimit { get; init; } = 10;

    /// <summary>Retention applied only to MassTransit error and skipped queues.</summary>
    public TimeSpan FaultQueueRetention { get; init; } = TimeSpan.FromDays(14);

    /// <summary>Maximum number of messages retained in each error or skipped queue.</summary>
    public long FaultQueueMaxLength { get; init; } = 10_000;

    /// <summary>Maximum broker message payload, enforced by RabbitMQ deployment configuration.</summary>
    public int MaximumMessageBytes { get; init; } = 1_048_576;

    /// <summary>
    /// Explicitly approves the fully validated global policy for endpoints without a typed override.
    /// Set to false in services where every business consumer must declare its own policy.
    /// </summary>
    public bool AllowValidatedDefaultConsumerPolicy { get; init; } = true;

    /// <summary>
    /// Backward-compatible, endpoint-name keyed overrides. Every entry is validated against the
    /// endpoints actually configured at startup; stale or misspelled entries fail bus creation.
    /// Prefer <c>AddConsumerWithPolicy&lt;TConsumer&gt;</c> for new consumers.
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

    /// <summary>Optional maximum messages accepted during <see cref="RateLimitInterval"/>.</summary>
    public int? RateLimit { get; init; }

    public TimeSpan? RateLimitInterval { get; init; }

    /// <summary>
    /// Enables RabbitMQ single-active-consumer semantics. Ordering-sensitive endpoints must also
    /// configure prefetch and concurrency as one.
    /// </summary>
    public bool SingleActiveConsumer { get; init; }

    /// <summary>Marks a business-critical consumer that must never inherit a default policy.</summary>
    public bool IsCritical { get; init; }

    /// <summary>Requires serial processing and single-active-consumer broker semantics.</summary>
    public bool RequiresOrderedDelivery { get; init; }
}
