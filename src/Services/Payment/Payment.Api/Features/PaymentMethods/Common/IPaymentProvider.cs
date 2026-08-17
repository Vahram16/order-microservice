namespace Payment.Api.Features.PaymentMethods.Common;

internal interface IPaymentProvider
{
    Task<string> CreateCustomerAsync(
        Guid paymentCustomerId,
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PaymentMethodSetupSession> CreatePaymentMethodSetupAsync(
        Guid paymentCustomerId,
        string providerCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PaymentMethodSetupSession> GetPaymentMethodSetupAsync(
        string providerSetupIntentId,
        CancellationToken cancellationToken);

    Task<ProviderPaymentMethod> GetPaymentMethodAsync(
        string providerPaymentMethodId,
        CancellationToken cancellationToken);
}

internal sealed record PaymentMethodSetupSession(
    string ProviderSetupIntentId,
    string ClientSecret,
    string Status,
    string? ProviderCustomerId = null,
    string? ProviderPaymentMethodId = null);

internal sealed record ProviderPaymentMethod(
    string ProviderPaymentMethodId,
    string ProviderCustomerId,
    string Brand,
    string Last4,
    int ExpMonth,
    int ExpYear,
    string? WalletType);
