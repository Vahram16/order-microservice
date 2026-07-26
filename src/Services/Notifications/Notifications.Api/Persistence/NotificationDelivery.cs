namespace Notifications.Api.Persistence;

public sealed class NotificationDelivery
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid SourceEventId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string ProtectedPayload { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public Guid? LockId { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? AcceptedByProviderAtUtc { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
}
