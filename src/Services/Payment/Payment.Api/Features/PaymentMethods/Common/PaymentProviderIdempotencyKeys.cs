namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentProviderIdempotencyKeys
{
    public static string PaymentCustomer(Guid paymentCustomerId) =>
        $"payment-customer:{paymentCustomerId:N}";

    public static string PaymentMethodSetup(Guid requestId) =>
        $"payment-method-setup:{requestId:N}";
}
