using Microservices.Contracts;

namespace Payment.Api.Webhooks;

public sealed record ProcessStripeWebhook(Guid WebhookEventId) : IIntegrationCommand
{
    public const string EndpointName = "payment-process-stripe-webhook";
}
