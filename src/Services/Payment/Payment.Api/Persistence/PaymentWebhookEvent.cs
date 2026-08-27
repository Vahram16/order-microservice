namespace Payment.Api.Persistence;

internal sealed class PaymentWebhookEvent
{
    private PaymentWebhookEvent() { }

    public Guid Id { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? ProviderSetupIntentId { get; private set; }
    public string? ProviderPaymentIntentId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static PaymentWebhookEvent CreateSetup(
        Guid id,
        string providerEventId,
        string eventType,
        string providerSetupIntentId,
        DateTimeOffset receivedAt) =>
        Create(id, providerEventId, eventType, providerSetupIntentId, null, receivedAt);

    public static PaymentWebhookEvent CreateOrderPayment(
        Guid id,
        string providerEventId,
        string eventType,
        string providerPaymentIntentId,
        DateTimeOffset receivedAt) =>
        Create(id, providerEventId, eventType, null, providerPaymentIntentId, receivedAt);

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt ??= now;

    private static PaymentWebhookEvent Create(
        Guid id,
        string providerEventId,
        string eventType,
        string? providerSetupIntentId,
        string? providerPaymentIntentId,
        DateTimeOffset receivedAt)
    {
        if ((providerSetupIntentId is null) == (providerPaymentIntentId is null))
        {
            throw new ArgumentException("Exactly one provider object identifier must be supplied.");
        }

        return new PaymentWebhookEvent
        {
            Id = id,
            ProviderEventId = providerEventId,
            EventType = eventType,
            ProviderSetupIntentId = providerSetupIntentId,
            ProviderPaymentIntentId = providerPaymentIntentId,
            ReceivedAt = receivedAt
        };
    }
}
