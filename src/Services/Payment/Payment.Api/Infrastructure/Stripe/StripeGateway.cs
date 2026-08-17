using Microsoft.Extensions.Options;
using Stripe;

namespace Payment.Api.Infrastructure.Stripe;

internal sealed class StripeGateway : IStripeGateway
{
    private const string CustomerIdMetadata = "customer_id";
    private const string RequestIdMetadata = "request_id";
    private const string MakeDefaultMetadata = "make_default";

    private readonly StripeClient _client;
    private readonly string _webhookSecret;

    public StripeGateway(IOptions<StripeOptions> options)
    {
        var value = options.Value;
        _client = new StripeClient(value.SecretKey);
        _webhookSecret = value.WebhookSecret;
    }

    public async Task<string> CreateCustomerAsync(
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var service = new CustomerService(_client);
        var customer = await service.CreateAsync(
            new CustomerCreateOptions
            {
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CustomerIdMetadata] = customerId.ToString("D")
                }
            },
            new RequestOptions { IdempotencyKey = idempotencyKey },
            cancellationToken);

        return customer.Id;
    }

    public async Task<StripeSetupIntentSnapshot> CreateSetupIntentAsync(
        string stripeCustomerId,
        Guid customerId,
        Guid requestId,
        bool makeDefault,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var service = new SetupIntentService(_client);
        var setupIntent = await service.CreateAsync(
            new SetupIntentCreateOptions
            {
                Customer = stripeCustomerId,
                Usage = "off_session",
                PaymentMethodTypes = ["card"],
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CustomerIdMetadata] = customerId.ToString("D"),
                    [RequestIdMetadata] = requestId.ToString("D"),
                    [MakeDefaultMetadata] = makeDefault ? bool.TrueString : bool.FalseString
                }
            },
            new RequestOptions { IdempotencyKey = idempotencyKey },
            cancellationToken);

        return Map(setupIntent);
    }

    public async Task<StripeSetupIntentSnapshot> GetSetupIntentAsync(
        string setupIntentId,
        CancellationToken cancellationToken)
    {
        var service = new SetupIntentService(_client);
        var setupIntent = await service.GetAsync(setupIntentId, cancellationToken: cancellationToken);
        return Map(setupIntent);
    }

    public async Task<StripePaymentMethodSnapshot> GetPaymentMethodAsync(
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        var service = new PaymentMethodService(_client);
        var method = await service.GetAsync(paymentMethodId, cancellationToken: cancellationToken);
        return new StripePaymentMethodSnapshot(
            method.Id,
            method.Type,
            method.Card?.Brand,
            method.Card?.Last4,
            method.Card?.ExpMonth is { } month ? checked((int)month) : null,
            method.Card?.ExpYear is { } year ? checked((int)year) : null,
            method.Card?.Wallet?.Type);
    }

    public StripeWebhookEnvelope ConstructWebhookEvent(string payload, string signatureHeader)
    {
        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret);
        var objectId = stripeEvent.Data.Object?.Id ?? string.Empty;
        return new StripeWebhookEnvelope(stripeEvent.Id, stripeEvent.Type, objectId);
    }

    private static StripeSetupIntentSnapshot Map(SetupIntent setupIntent) =>
        new(
            setupIntent.Id,
            setupIntent.ClientSecret,
            setupIntent.Status,
            setupIntent.CustomerId,
            setupIntent.PaymentMethodId,
            setupIntent.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(setupIntent.Metadata, StringComparer.Ordinal));
}
