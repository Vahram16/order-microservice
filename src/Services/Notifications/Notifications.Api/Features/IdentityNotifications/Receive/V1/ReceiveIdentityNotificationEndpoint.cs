using MediatR;
using Notifications.Api.Security;

namespace Notifications.Api.Features.IdentityNotifications.Receive.V1;

internal static class ReceiveIdentityNotificationEndpoint
{
    public static IEndpointRouteBuilder MapIdentityNotificationIngress(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/internal/v1/notifications/identity",
                HandleAsync)
            .AllowAnonymous()
            .AddEndpointFilter<InternalApiKeyEndpointFilter>()
            .RequireRateLimiting("notification-ingress")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .WithName("ReceiveIdentityNotification")
            .WithSummary("Durably accept an Identity notification for asynchronous delivery.");

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ReceiveIdentityNotificationRequest request,
        HttpRequest httpRequest,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();
        try
        {
            var result = await sender.Send(
                new ReceiveIdentityNotificationCommand(
                    idempotencyKey,
                    request.EventId,
                    request.Template,
                    request.Recipient,
                    request.ActionUrl,
                    request.ExpiresAtUtc),
                cancellationToken);

            return Results.Accepted(value: new
            {
                eventId = request.EventId,
                duplicate = result == NotificationAcceptanceResult.Duplicate
            });
        }
        catch (ConflictingNotificationIdempotencyException)
        {
            return Results.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflicting idempotency key",
                Detail = "The event identifier is already associated with a different payload."
            });
        }
    }
}
