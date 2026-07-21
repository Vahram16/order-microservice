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
}
