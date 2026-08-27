using System.Text;
using Microservices.Application.Messaging;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Persistence;

namespace Payment.Api.Webhooks;

internal static class StripeWebhookEndpoint
{
    private const long MaximumPayloadBytes = 256 * 1024;

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/webhooks/stripe", async (
                HttpRequest request,
                PaymentDbContext dbContext,
                IPaymentWebhookVerifier verifier,
                IIntegrationCommandSender<ProcessStripeWebhook> commandSender,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (request.ContentLength is > MaximumPayloadBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySizeFeature)
                {
                    bodySizeFeature.MaxRequestBodySize = MaximumPayloadBytes;
                }

                if (!request.Headers.TryGetValue("Stripe-Signature", out var signatures) || signatures.Count != 1) return Results.BadRequest();

                string payload;
                using (var reader = new StreamReader(request.Body, Encoding.UTF8, false, 4096, leaveOpen: true))
                {
                    payload = await reader.ReadToEndAsync(cancellationToken);
                }

                PaymentWebhookNotification? notification;
                try { notification = verifier.Verify(payload, signatures[0]!); }
                catch (PaymentWebhookVerificationException) { return Results.BadRequest(); }
                if (notification is null) return Results.Ok();

                var id = Guid.NewGuid();
                var now = timeProvider.GetUtcNow();
                var webhookEvent = notification.ObjectKind switch
                {
                    PaymentWebhookObjectKind.PaymentMethodSetup => PaymentWebhookEvent.CreateSetup(id, notification.ProviderEventId, notification.EventType, notification.ProviderObjectId, now),
                    PaymentWebhookObjectKind.OrderPayment => PaymentWebhookEvent.CreateOrderPayment(id, notification.ProviderEventId, notification.EventType, notification.ProviderObjectId, now),
                    _ => throw new ArgumentOutOfRangeException(nameof(notification.ObjectKind), notification.ObjectKind, "Unknown payment webhook object kind.")
                };
                dbContext.PaymentWebhookEvents.Add(webhookEvent);
                await commandSender.SendAsync(
                    new ProcessStripeWebhook(webhookEvent.Id),
                    new IntegrationMessageMetadata(MessageId: webhookEvent.Id),
                    cancellationToken);

                try { await dbContext.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException exception) when (exception.IsUniqueConstraintViolation(PaymentDatabaseConstraints.ProviderWebhookEvent))
                {
                    return Results.Ok();
                }

                return Results.Ok();
            })
            .WithName("StripeWebhook")
            .WithSummary("Receives signature-verified Stripe webhook events.")
            .AllowAnonymous();
}
