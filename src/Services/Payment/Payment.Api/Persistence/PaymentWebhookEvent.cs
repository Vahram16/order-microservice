namespace Payment.Api.Persistence;

internal sealed class PaymentWebhookEvent
{
    private PaymentWebhookEvent() { }

    public Guid Id { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string ProviderSetupIntentId { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid? ProcessingToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public string? LastErrorCode { get; private set; }

    public static PaymentWebhookEvent Create(
        Guid id,
        string providerEventId,
        string eventType,
        string providerSetupIntentId,
        DateTimeOffset receivedAt) =>
        new()
        {
            Id = id,
            ProviderEventId = providerEventId,
            EventType = eventType,
            ProviderSetupIntentId = providerSetupIntentId,
            ReceivedAt = receivedAt,
            NextAttemptAt = receivedAt
        };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        ProcessingToken = null;
        LeaseExpiresAt = null;
        LastErrorCode = null;
    }

    public void MarkFailed(DateTimeOffset now, string errorCode, int maximumAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttempts);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        AttemptCount++;
        ProcessingToken = null;
        LeaseExpiresAt = null;
        LastErrorCode = errorCode;

        if (AttemptCount >= maximumAttempts)
        {
            DeadLetteredAt = now;
            return;
        }

        var delaySeconds = Math.Min(300, 1 << Math.Min(AttemptCount, 8));
        NextAttemptAt = now.AddSeconds(delaySeconds);
    }
}
