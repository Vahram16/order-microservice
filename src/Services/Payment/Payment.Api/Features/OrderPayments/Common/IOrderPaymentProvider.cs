namespace Payment.Api.Features.OrderPayments.Common;

internal interface IOrderPaymentProvider
{
    Task<OrderPaymentProviderSession> CreateAsync(Guid orderId, string providerCustomerId, string providerPaymentMethodId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderPaymentProviderSession> ConfirmAsync(string providerPaymentIntentId, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderPaymentProviderSession> CaptureAsync(string providerPaymentIntentId, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderPaymentProviderSession> GetAsync(string providerPaymentIntentId, CancellationToken cancellationToken);
    Task<OrderPaymentProviderSession> CancelAsync(string providerPaymentIntentId, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderPaymentRefundSession> RefundAsync(string providerPaymentIntentId, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderPaymentRefundSession> GetRefundAsync(string providerRefundId, CancellationToken cancellationToken);
}

internal sealed record OrderPaymentProviderSession(
    string ProviderPaymentIntentId,
    string Status,
    string? ClientSecret,
    string? ProviderCustomerId,
    string? ProviderPaymentMethodId,
    decimal Amount,
    string CurrencyCode);

internal sealed record OrderPaymentRefundSession(
    string ProviderRefundId,
    string? ProviderPaymentIntentId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? FailureReason);
