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
        endpoints.MapPost(
                "/webhooks/stripe",
                async (
                    HttpRequest request,
                    PaymentDbContext dbContext,
                    IPaymentWebhookVerifier verifier,
                    IIntegrationCommandSender<ProcessStripeWebhook> commandSender,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    if (request.ContentLength is > MaximumPayloadBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is
                        { IsReadOnly: false } bodySizeFeature)
                    {
                        bodySizeFeature.MaxRequestBodySize = MaximumPayloadBytes;
                    }

                    if (!request.Headers.TryGetValue("Stripe-Signature", out var signatures) ||
                        signatures.Count != 1)
                    {
                        return Results.BadRequest();
                    }

                    string payload;
                    using (var reader = new StreamReader(
                               request.Body,
                               Encoding.UTF8,
                               detectEncodingFromByteOrderMarks: false,
                               bufferSize: 4096,
                               leaveOpen: true))
                    {
                        payload = await reader.ReadToEndAsync(cancellationToken);
                    }

                    PaymentWebhookNotification? notification;
                    try
                    {
                        notification = verifier.Verify(payload, signatures[0]!);
                    }
                    catch (PaymentWebhookVerificationException)
                    {
                        return Results.BadRequest();
                    }

                    if (notification is null)
                    {
                        return Results.Ok();
                    }

                    var webhookEvent = PaymentWebhookEvent.Create(
                        Guid.NewGuid(),
                        notification.ProviderEventId,
                        notification.EventType,
                        notification.ProviderSetupIntentId,
                        timeProvider.GetUtcNow());
                    dbContext.PaymentWebhookEvents.Add(webhookEvent);

                    // Use the EF bus outbox in this same DbContext scope. SaveChanges atomically
                    // persists both the durable webhook receipt and the processing command; RabbitMQ
                    // availability is therefore not on the Stripe HTTP acknowledgement path.
                    await commandSender.SendAsync(
                        new ProcessStripeWebhook(webhookEvent.Id),
                        new IntegrationMessageMetadata(MessageId: webhookEvent.Id),
                        cancellationToken);

                    try
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException exception) when (
                        exception.IsUniqueConstraintViolation(
                            PaymentDatabaseConstraints.ProviderWebhookEvent))
                    {
                        // The first delivery already owns the durable receipt and outbox message.
                        // The duplicate delivery is acknowledged without publishing a second command.
                        return Results.Ok();
                    }

                    return Results.Ok();
                })
            .WithName("StripeWebhook")
            .WithSummary("Receives signature-verified Stripe webhook events.")
            .AllowAnonymous();
}
