namespace Identity.Api.Notifications;

public sealed class IdentityNotificationOutboxMessage
{
    public Guid Id { get; init; }

    public required string DeduplicationKey { get; init; }

    public required string ProtectedPayload { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset AvailableAtUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public Guid? LockId { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; set; }

    public string? LastError { get; set; }
}
