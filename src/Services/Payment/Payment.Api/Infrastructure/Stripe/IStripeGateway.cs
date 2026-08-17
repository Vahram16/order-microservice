namespace Payment.Api.Infrastructure.Stripe;

internal interface IStripeGateway
{
    Task<string> CreateCustomerAsync(
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StripeSetupIntentSnapshot> CreateSetupIntentAsync(
        string stripeCustomerId,
        Guid customerId,
        Guid requestId,
        bool makeDefault,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StripeSetupIntentSnapshot> GetSetupIntentAsync(
        string setupIntentId,
        CancellationToken cancellationToken);

    Task<StripePaymentMethodSnapshot> GetPaymentMethodAsync(
        string paymentMethodId,
        CancellationToken cancellationToken);

    StripeWebhookEnvelope ConstructWebhookEvent(string payload, string signatureHeader);
}

internal sealed record StripeSetupIntentSnapshot(
    string Id,
    string? ClientSecret,
    string Status,
    string? CustomerId,
    string? PaymentMethodId,
    IReadOnlyDictionary<string, string> Metadata);

internal sealed record StripePaymentMethodSnapshot(
    string Id,
    string Type,
    string? Brand,
    string? Last4,
    int? ExpMonth,
    int? ExpYear,
    string? WalletType);

internal sealed record StripeWebhookEnvelope(string EventId, string EventType, string ObjectId);
