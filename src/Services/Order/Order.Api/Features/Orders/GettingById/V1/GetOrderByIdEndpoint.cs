using MediatR;
using Microservices.Security;
using Order.Api.Features.Orders.Common;
using Order.Api.Infrastructure;

namespace Order.Api.Features.Orders.GettingById.V1;

internal static class GetOrderByIdEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{orderId:guid}", async (
                Guid orderId,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var identity = CurrentOrderIdentity.From(context.User);
                if (identity.IsFailure)
                {
                    return OrderHttpResults.Problem(identity.Error, context);
                }

                var result = await sender.Send(new GetOrderByIdQuery(
                    orderId,
                    identity.Value.Provider,
                    identity.Value.Subject), cancellationToken);
                return result.Match<IResult>(Results.Ok, error => OrderHttpResults.Problem(error, context));
            })
            .WithName("GetOrderById")
            .WithSummary("Gets an order owned by the authenticated customer.")
            .RequireAuthorization(RolePolicy.For(OrderAuthorization.ReadRole))
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
