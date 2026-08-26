using MediatR;
using Microservices.Security;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.Deleting.V1;

internal static class DeleteProductEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete(
                "/{productId:guid}",
                async (
                    Guid productId,
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var expectedVersion = ProductHttp.ReadExpectedVersion(httpContext.Request, productId);
                    if (expectedVersion.IsFailure)
                    {
                        return ProductHttpResults.Problem(expectedVersion.Error, httpContext);
                    }

                    var result = await sender.Send(
                        new DeleteProductCommand(productId, expectedVersion.Value),
                        cancellationToken);

                    return result.Match<IResult>(
                        Results.NoContent,
                        error => ProductHttpResults.Problem(error, httpContext));
                })
            .WithName("DeleteProduct")
            .WithSummary("Deletes a product using optimistic concurrency.")
            .RequireAuthorization(RolePolicy.For(ProductAuthorization.ManageRole))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
