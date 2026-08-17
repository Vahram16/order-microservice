namespace Payment.Api.Webhooks;

internal interface IPaymentWebhookVerifier
{
    PaymentWebhookNotification? Verify(string payload, string signature);
}

internal sealed record PaymentWebhookNotification(
    string ProviderEventId,
    string EventType,
    string ProviderSetupIntentId);

internal sealed class PaymentWebhookVerificationException(Exception innerException)
    : Exception("Payment webhook signature verification failed.", innerException);
