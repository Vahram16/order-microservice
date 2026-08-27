using MediatR;
using Microservices.Security;
using Order.Api.Features.Orders.Common;
using Order.Api.Infrastructure;

namespace Order.Api.Features.Orders.Creating.V1;

internal static class CreateOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (
                CreateOrderRequest request,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var identity = CurrentOrderIdentity.From(context.User);
                if (identity.IsFailure)
                {
                    return OrderHttpResults.Problem(identity.Error, context);
                }

                if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var values) ||
                    values.Count != 1 ||
                    !Guid.TryParse(values[0], out var idempotencyKey) ||
                    idempotencyKey == Guid.Empty)
                {
                    return OrderHttpResults.Problem(OrderApplicationErrors.InvalidIdempotencyKey, context);
                }

                var result = await sender.Send(new CreateOrderCommand(
                    idempotencyKey,
                    identity.Value.Provider,
                    identity.Value.Subject,
                    request.Items,
                    request.PaymentMethodId,
                    request.ShippingAddress), cancellationToken);

                return result.Match<IResult>(
                    order => Results.Accepted($"/api/v1/orders/{order.Id}", order),
                    error => OrderHttpResults.Problem(error, context));
            })
            .WithName("CreateOrder")
            .WithSummary("Creates an idempotent order and starts its durable checkout workflow.")
            .RequireAuthorization(RolePolicy.For(OrderAuthorization.WriteRole))
            .Produces<OrderResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
