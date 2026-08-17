namespace Payment.Api.Persistence;

internal sealed class PaymentWebhookEvent
{
    private PaymentWebhookEvent() { }

    public Guid Id { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string ProviderSetupIntentId { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

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
            ReceivedAt = receivedAt
        };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt ??= now;
    }
}
