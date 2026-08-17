using Microsoft.Extensions.Options;
using Payment.Api.Features.PaymentMethods.Common;
using Stripe;

namespace Payment.Api.Infrastructure.Stripe;

internal sealed class StripePaymentProvider : IPaymentProvider
{
    private const string PaymentCustomerMetadata = "payment_customer_id";
    private readonly StripeClient _client;

    public StripePaymentProvider(IOptions<StripeOptions> options)
    {
        _client = new StripeClient(options.Value.SecretKey);
    }

    public async Task<string> CreateCustomerAsync(
        Guid paymentCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var service = new CustomerService(_client);
            var customer = await service.CreateAsync(
                new CustomerCreateOptions
                {
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [PaymentCustomerMetadata] = paymentCustomerId.ToString("D")
                    }
                },
                new RequestOptions { IdempotencyKey = idempotencyKey },
                cancellationToken);
            return customer.Id;
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderException("stripe.customer_create_failed", exception);
        }
    }

    public async Task<PaymentMethodSetupSession> CreatePaymentMethodSetupAsync(
        Guid paymentCustomerId,
        string providerCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var service = new SetupIntentService(_client);
            var intent = await service.CreateAsync(
                new SetupIntentCreateOptions
                {
                    Customer = providerCustomerId,
                    Usage = "off_session",
                    PaymentMethodTypes = ["card"],
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [PaymentCustomerMetadata] = paymentCustomerId.ToString("D")
                    }
                },
                new RequestOptions { IdempotencyKey = idempotencyKey },
                cancellationToken);
            return Map(intent);
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderException("stripe.setup_intent_create_failed", exception);
        }
    }

    public async Task<PaymentMethodSetupSession> GetPaymentMethodSetupAsync(
        string providerSetupIntentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var service = new SetupIntentService(_client);
            var intent = await service.GetAsync(
                providerSetupIntentId,
                cancellationToken: cancellationToken);
            return Map(intent);
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderException("stripe.setup_intent_get_failed", exception);
        }
    }

    public async Task<ProviderPaymentMethod> GetPaymentMethodAsync(
        string providerPaymentMethodId,
        CancellationToken cancellationToken)
    {
        try
        {
            var service = new PaymentMethodService(_client);
            var method = await service.GetAsync(
                providerPaymentMethodId,
                cancellationToken: cancellationToken);

            if (!string.Equals(method.Type, "card", StringComparison.Ordinal) ||
                method.Card is null ||
                string.IsNullOrWhiteSpace(method.CustomerId))
            {
                throw new PaymentProviderException(
                    "stripe.unsupported_payment_method",
                    new InvalidOperationException("Stripe returned an unsupported payment method shape."));
            }

            return new ProviderPaymentMethod(
                method.Id,
                method.CustomerId,
                method.Card.Brand,
                method.Card.Last4,
                checked((int)method.Card.ExpMonth),
                checked((int)method.Card.ExpYear),
                method.Card.Wallet?.Type);
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderException("stripe.payment_method_get_failed", exception);
        }
    }

    private static PaymentMethodSetupSession Map(SetupIntent intent) =>
        new(
            intent.Id,
            intent.ClientSecret ?? string.Empty,
            intent.Status,
            intent.CustomerId,
            intent.PaymentMethodId);
}
