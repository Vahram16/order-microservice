using MediatR;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.GettingById.V1;

internal static class GetProductByIdEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet(
                "/{productId:guid}",
                async (
                    Guid productId,
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetProductByIdQuery(productId),
                        cancellationToken);

                    return result.Match<IResult>(
                        product =>
                        {
                            ProductHttp.WriteEtag(httpContext.Response, product.Id, product.Version);
                            return Results.Ok(product);
                        },
                        error => ProductHttpResults.Problem(error, httpContext));
                })
            .WithName("GetProductById")
            .WithSummary("Gets a product by identifier.")
            .RequireAuthorization()
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
