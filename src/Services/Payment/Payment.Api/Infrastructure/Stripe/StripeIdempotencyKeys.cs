namespace Payment.Api.Infrastructure.Stripe;

internal static class StripeIdempotencyKeys
{
    public static string Customer(Guid customerId) =>
        $"payment-customer:{customerId:N}";

    public static string SetupIntent(Guid customerId, Guid requestId) =>
        $"payment-method-setup:{customerId:N}:{requestId:N}";
}
