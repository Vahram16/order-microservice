using Microsoft.Extensions.Options;
using Payment.Api.Webhooks;
using Stripe;

namespace Payment.Api.Infrastructure.Stripe;

internal sealed class StripeWebhookVerifier(IOptions<StripeOptions> options) : IPaymentWebhookVerifier
{
    private const string SetupIntentSucceeded = "setup_intent.succeeded";
    private static readonly HashSet<string> OrderPaymentEvents = new(StringComparer.Ordinal)
    {
        "payment_intent.amount_capturable_updated",
        "payment_intent.payment_failed",
        "payment_intent.processing",
        "payment_intent.requires_action",
        "payment_intent.succeeded",
        "payment_intent.canceled"
    };
    private static readonly HashSet<string> RefundEvents = new(StringComparer.Ordinal)
    {
        "refund.created",
        "refund.updated",
        "refund.failed"
    };

    private readonly string _webhookSecret = options.Value.WebhookSecret;

    public PaymentWebhookNotification? Verify(string payload, string signature)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, _webhookSecret);
            if (string.Equals(stripeEvent.Type, SetupIntentSucceeded, StringComparison.Ordinal) &&
                stripeEvent.Data.Object is SetupIntent setupIntent &&
                !string.IsNullOrWhiteSpace(setupIntent.Id))
            {
                return new PaymentWebhookNotification(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    PaymentWebhookObjectKind.PaymentMethodSetup,
                    setupIntent.Id);
            }

            if (OrderPaymentEvents.Contains(stripeEvent.Type) &&
                stripeEvent.Data.Object is PaymentIntent paymentIntent &&
                !string.IsNullOrWhiteSpace(paymentIntent.Id))
            {
                return new PaymentWebhookNotification(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    PaymentWebhookObjectKind.OrderPayment,
                    paymentIntent.Id);
            }

            if (RefundEvents.Contains(stripeEvent.Type) &&
                stripeEvent.Data.Object is Refund refund &&
                !string.IsNullOrWhiteSpace(refund.Id))
            {
                return new PaymentWebhookNotification(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    PaymentWebhookObjectKind.OrderPaymentRefund,
                    refund.Id);
            }

            return null;
        }
        catch (StripeException exception)
        {
            throw new PaymentWebhookVerificationException(exception);
        }
    }
}
