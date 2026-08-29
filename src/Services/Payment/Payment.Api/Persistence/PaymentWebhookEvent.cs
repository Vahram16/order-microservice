namespace Payment.Api.Persistence;

internal sealed class PaymentWebhookEvent
{
    private PaymentWebhookEvent() { }

    public Guid Id { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string? ProviderSetupIntentId { get; private set; }
    public string? ProviderPaymentIntentId { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static PaymentWebhookEvent CreateSetup(
        Guid id,
        string providerEventId,
        string eventType,
        string providerSetupIntentId,
        DateTimeOffset receivedAt) =>
        Create(id, providerEventId, eventType, providerSetupIntentId, null, null, receivedAt);

    public static PaymentWebhookEvent CreateOrderPayment(
        Guid id,
        string providerEventId,
        string eventType,
        string providerPaymentIntentId,
        DateTimeOffset receivedAt) =>
        Create(id, providerEventId, eventType, null, providerPaymentIntentId, null, receivedAt);

    public static PaymentWebhookEvent CreateOrderPaymentRefund(
        Guid id,
        string providerEventId,
        string eventType,
        string providerRefundId,
        DateTimeOffset receivedAt) =>
        Create(id, providerEventId, eventType, null, null, providerRefundId, receivedAt);

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt ??= now;

    private static PaymentWebhookEvent Create(
        Guid id,
        string providerEventId,
        string eventType,
        string? providerSetupIntentId,
        string? providerPaymentIntentId,
        string? providerRefundId,
        DateTimeOffset receivedAt)
    {
        var suppliedIdentifiers = new[] { providerSetupIntentId, providerPaymentIntentId, providerRefundId }
            .Count(value => value is not null);
        if (suppliedIdentifiers != 1)
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
            ProviderRefundId = providerRefundId,
            ReceivedAt = receivedAt
        };
    }
}
