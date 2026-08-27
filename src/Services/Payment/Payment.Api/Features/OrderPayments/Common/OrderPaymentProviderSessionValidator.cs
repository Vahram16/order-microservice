namespace Payment.Api.Features.OrderPayments.Common;

internal static class OrderPaymentProviderSessionValidator
{
    public static bool Matches(
        OrderPaymentProviderSession session,
        string providerPaymentIntentId,
        string providerCustomerId,
        string providerPaymentMethodId,
        decimal amount,
        string currencyCode) =>
        string.Equals(session.ProviderPaymentIntentId, providerPaymentIntentId, StringComparison.Ordinal) &&
        string.Equals(session.ProviderCustomerId, providerCustomerId, StringComparison.Ordinal) &&
        string.Equals(session.ProviderPaymentMethodId, providerPaymentMethodId, StringComparison.Ordinal) &&
        session.Amount == amount &&
        string.Equals(session.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase);
}
