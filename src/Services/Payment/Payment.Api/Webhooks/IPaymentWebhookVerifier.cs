namespace Payment.Api.Webhooks;

internal interface IPaymentWebhookVerifier
{
    PaymentWebhookNotification? Verify(string payload, string signature);
}

internal enum PaymentWebhookObjectKind
{
    PaymentMethodSetup = 1,
    OrderPayment = 2,
    OrderPaymentRefund = 3
}

internal sealed record PaymentWebhookNotification(
    string ProviderEventId,
    string EventType,
    PaymentWebhookObjectKind ObjectKind,
    string ProviderObjectId);

internal sealed class PaymentWebhookVerificationException(Exception innerException)
    : Exception("Payment webhook signature verification failed.", innerException);
