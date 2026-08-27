using Inventory.Api.Features.Inventory.Common;
using MediatR;
using Microservices.Security;

namespace Inventory.Api.Features.Inventory.SettingStock.V1;

internal static class SetInventoryStockEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{productId:guid}", async (
                Guid productId,
                SetInventoryStockRequest request,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var expected = InventoryHttp.ReadOptionalExpectedVersion(context.Request, productId);
                if (expected.IsFailure)
                {
                    return InventoryHttpResults.Problem(expected.Error, context);
                }

                var result = await sender.Send(new SetInventoryStockCommand(productId, request.OnHand, expected.Value), cancellationToken);
                return result.Match<IResult>(
                    inventory =>
                    {
                        InventoryHttp.WriteEtag(context.Response, inventory.ProductId, inventory.Version);
                        return Results.Ok(inventory);
                    },
                    error => InventoryHttpResults.Problem(error, context));
            })
            .WithName("SetInventoryStock")
            .WithSummary("Creates inventory or updates existing on-hand stock with optimistic concurrency.")
            .RequireAuthorization(RolePolicy.For(InventoryAuthorization.ManageRole))
            .Produces<InventoryResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
