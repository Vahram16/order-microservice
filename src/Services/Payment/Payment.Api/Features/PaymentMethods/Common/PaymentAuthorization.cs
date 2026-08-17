namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentAuthorization
{
    public const string Role = "payment-user";
    public const string ReadScope = "payments.methods.read";
    public const string WriteScope = "payments.methods.write";
}
