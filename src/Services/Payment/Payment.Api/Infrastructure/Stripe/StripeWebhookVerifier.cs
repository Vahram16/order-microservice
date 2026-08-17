using Microsoft.Extensions.Options;
using Payment.Api.Webhooks;
using Stripe;

namespace Payment.Api.Infrastructure.Stripe;

internal sealed class StripeWebhookVerifier(IOptions<StripeOptions> options)
    : IPaymentWebhookVerifier
{
    private const string SetupIntentSucceeded = "setup_intent.succeeded";
    private readonly string _webhookSecret = options.Value.WebhookSecret;

    public PaymentWebhookNotification? Verify(string payload, string signature)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _webhookSecret);

            if (!string.Equals(stripeEvent.Type, SetupIntentSucceeded, StringComparison.Ordinal))
            {
                return null;
            }

            if (stripeEvent.Data.Object is not SetupIntent setupIntent ||
                string.IsNullOrWhiteSpace(setupIntent.Id))
            {
                return null;
            }

            return new PaymentWebhookNotification(
                stripeEvent.Id,
                stripeEvent.Type,
                setupIntent.Id);
        }
        catch (StripeException exception)
        {
            throw new PaymentWebhookVerificationException(exception);
        }
    }
}
