namespace Payment.Api.Persistence;

internal sealed class StripeWebhookInboxEntry
{
    public string EventId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string ObjectId { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public Guid? ProcessingLeaseId { get; set; }
    public DateTimeOffset? ProcessingLeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
