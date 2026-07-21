namespace Identity.Api.Configuration;

public sealed class IdentityNotificationOptions
{
    public const string SectionName = "IdentityNotifications";

    public IdentityNotificationProvider Provider { get; init; }

    public string? PublicOrigin { get; init; }

    public string? WebhookEndpoint { get; init; }

    public string? WebhookApiKey { get; init; }

    public TimeSpan DispatchInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan DeduplicationWindow { get; init; } = TimeSpan.FromMinutes(5);

    public int BatchSize { get; init; } = 20;

    public int MaximumAttempts { get; init; } = 12;
}

public enum IdentityNotificationProvider
{
    None,
    DevelopmentLog,
    Webhook
}
