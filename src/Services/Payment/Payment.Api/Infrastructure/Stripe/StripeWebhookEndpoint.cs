using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stripe;
using Payment.Api.Persistence;

namespace Payment.Api.Infrastructure.Stripe;

internal static class StripeWebhookEndpoint
{
    private const long MaximumPayloadBytes = 256 * 1024;

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(
                "/webhooks/stripe",
                async (
                    HttpRequest request,
                    PaymentDbContext dbContext,
                    IStripeGateway stripeGateway,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    if (request.ContentLength is > MaximumPayloadBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    if (!request.Headers.TryGetValue("Stripe-Signature", out var signature) ||
                        signature.Count != 1)
                    {
                        return Results.BadRequest();
                    }

                    using var reader = new StreamReader(request.Body);
                    var payload = await reader.ReadToEndAsync(cancellationToken);

                    StripeWebhookEnvelope envelope;
                    try
                    {
                        envelope = stripeGateway.ConstructWebhookEvent(payload, signature[0]!);
                    }
                    catch (StripeException)
                    {
                        return Results.BadRequest();
                    }

                    if (!string.Equals(envelope.EventType, Events.SetupIntentSucceeded, StringComparison.Ordinal))
                    {
                        return Results.Ok();
                    }

                    if (string.IsNullOrWhiteSpace(envelope.ObjectId))
                    {
                        return Results.BadRequest();
                    }

                    dbContext.StripeWebhookInbox.Add(new StripeWebhookInboxEntry
                    {
                        EventId = envelope.EventId,
                        EventType = envelope.EventType,
                        ObjectId = envelope.ObjectId,
                        ReceivedAtUtc = timeProvider.GetUtcNow()
                    });

                    try
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException exception) when (
                        exception.InnerException is PostgresException
                        {
                            SqlState: PostgresErrorCodes.UniqueViolation
                        })
                    {
                        return Results.Ok();
                    }

                    return Results.Ok();
                })
            .WithName("StripeWebhook")
            .WithSummary("Receives verified Stripe events and durably enqueues supported events.")
            .AllowAnonymous();
}
