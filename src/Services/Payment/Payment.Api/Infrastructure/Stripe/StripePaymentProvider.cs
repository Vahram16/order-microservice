using System.Net;
using Microsoft.Extensions.Options;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Stripe;

namespace Payment.Api.Infrastructure.Stripe;

internal sealed class StripePaymentProvider : IPaymentProvider, IOrderPaymentProvider
{
    private const string PaymentCustomerMetadata = "payment_customer_id";
    private const string OrderMetadata = "order_id";
    private readonly StripeClient _client;

    public StripePaymentProvider(IOptions<StripeOptions> options) : this(new StripeClient(options.Value.SecretKey)) { }
    internal StripePaymentProvider(StripeClient client) { ArgumentNullException.ThrowIfNull(client); _client = client; }

    public async Task<string> CreateCustomerAsync(Guid paymentCustomerId, string idempotencyKey, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var service = new CustomerService(_client);
            var customer = await service.CreateAsync(
                new CustomerCreateOptions { Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [PaymentCustomerMetadata] = paymentCustomerId.ToString("D") } },
                new RequestOptions { IdempotencyKey = idempotencyKey }, token);
            return customer.Id;
        }, "stripe.customer_create_failed", cancellationToken);

    public async Task<PaymentMethodSetupSession> CreatePaymentMethodSetupAsync(Guid paymentCustomerId, string providerCustomerId, string idempotencyKey, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var service = new SetupIntentService(_client);
            var intent = await service.CreateAsync(
                new SetupIntentCreateOptions
                {
                    Customer = providerCustomerId,
                    Usage = "off_session",
                    PaymentMethodTypes = ["card"],
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [PaymentCustomerMetadata] = paymentCustomerId.ToString("D") }
                },
                new RequestOptions { IdempotencyKey = idempotencyKey }, token);
            return Map(intent);
        }, "stripe.setup_intent_create_failed", cancellationToken);

    public async Task<PaymentMethodSetupSession> GetPaymentMethodSetupAsync(string providerSetupIntentId, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var intent = await new SetupIntentService(_client).GetAsync(providerSetupIntentId, cancellationToken: token);
            return Map(intent);
        }, "stripe.setup_intent_get_failed", cancellationToken);

    public async Task<ProviderPaymentMethod> GetPaymentMethodAsync(string providerPaymentMethodId, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var method = await new PaymentMethodService(_client).GetAsync(providerPaymentMethodId, cancellationToken: token);
            if (!string.Equals(method.Type, "card", StringComparison.Ordinal) || method.Card is null || string.IsNullOrWhiteSpace(method.CustomerId))
            {
                throw PaymentProviderException.Permanent("stripe.unsupported_payment_method", new InvalidOperationException("Stripe returned an unsupported payment method shape."));
            }
            return new ProviderPaymentMethod(method.Id, method.CustomerId, method.Card.Brand, method.Card.Last4, checked((int)method.Card.ExpMonth), checked((int)method.Card.ExpYear), method.Card.Wallet?.Type);
        }, "stripe.payment_method_get_failed", cancellationToken);

    public async Task<OrderPaymentProviderSession> CreateAsync(
        Guid orderId,
        string providerCustomerId,
        string providerPaymentMethodId,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var intent = await new PaymentIntentService(_client).CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = ToMinorUnits(amount),
                    Currency = currencyCode.ToLowerInvariant(),
                    Customer = providerCustomerId,
                    PaymentMethod = providerPaymentMethodId,
                    PaymentMethodTypes = ["card"],
                    CaptureMethod = "manual",
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [OrderMetadata] = orderId.ToString("D") }
                },
                new RequestOptions { IdempotencyKey = idempotencyKey }, token);
            return Map(intent);
        }, "stripe.payment_intent_create_failed", cancellationToken);

    public async Task<OrderPaymentProviderSession> ConfirmAsync(string providerPaymentIntentId, string idempotencyKey, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var intent = await new PaymentIntentService(_client).ConfirmAsync(
                providerPaymentIntentId,
                new PaymentIntentConfirmOptions(),
                new RequestOptions { IdempotencyKey = idempotencyKey },
                token);
            return Map(intent);
        }, "stripe.payment_intent_confirm_failed", cancellationToken);

    public async Task<OrderPaymentProviderSession> GetAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var intent = await new PaymentIntentService(_client).GetAsync(providerPaymentIntentId, cancellationToken: token);
            return Map(intent);
        }, "stripe.payment_intent_get_failed", cancellationToken);

    public async Task<OrderPaymentProviderSession> CancelAsync(string providerPaymentIntentId, string idempotencyKey, CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var intent = await new PaymentIntentService(_client).CancelAsync(
                providerPaymentIntentId,
                options: null,
                requestOptions: new RequestOptions { IdempotencyKey = idempotencyKey },
                cancellationToken: token);
            return Map(intent);
        }, "stripe.payment_intent_cancel_failed", cancellationToken);

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, string failureCode, CancellationToken cancellationToken)
    {
        try { return await operation(cancellationToken); }
        catch (StripeException exception) { throw TranslateStripeException(failureCode, exception); }
        catch (HttpRequestException exception) { throw PaymentProviderException.Transient(failureCode, exception); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested) { throw PaymentProviderException.Transient(failureCode, exception); }
    }

    private static PaymentProviderException TranslateStripeException(string failureCode, StripeException exception)
    {
        var shouldRetry = GetStripeShouldRetry(exception);
        var isTransient = shouldRetry ?? (exception.HttpStatusCode == HttpStatusCode.Conflict || exception.HttpStatusCode == HttpStatusCode.TooManyRequests || (int)exception.HttpStatusCode >= 500);
        return isTransient ? PaymentProviderException.Transient(failureCode, exception) : PaymentProviderException.Permanent(failureCode, exception);
    }

    private static bool? GetStripeShouldRetry(StripeException exception)
    {
        if (exception.StripeResponse?.Headers.TryGetValues("Stripe-Should-Retry", out var values) != true || values is null) return null;
        return bool.TryParse(values.FirstOrDefault(), out var shouldRetry) ? shouldRetry : null;
    }

    private static long ToMinorUnits(decimal amount) => checked(decimal.ToInt64(amount * 100m));
    private static PaymentMethodSetupSession Map(SetupIntent intent) => new(intent.Id, intent.ClientSecret ?? string.Empty, intent.Status, intent.CustomerId, intent.PaymentMethodId);
    private static OrderPaymentProviderSession Map(PaymentIntent intent) => new(intent.Id, intent.Status, intent.ClientSecret, intent.CustomerId, intent.PaymentMethodId, intent.Amount, intent.Currency.ToUpperInvariant());
}
