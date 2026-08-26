using MediatR;
using Microservices.Security;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.Listing.V1;

internal static class ListProductsEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet(
                "/",
                async (
                    int? page,
                    int? pageSize,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var query = new ListProductsQuery(
                        page ?? 1,
                        pageSize ?? 20);
                    var response = await sender.Send(query, cancellationToken);
                    return Results.Ok(response);
                })
            .WithName("ListProducts")
            .WithSummary("Lists products using bounded pagination.")
            .RequireAuthorization(RolePolicy.For(ProductAuthorization.ReadRole))
            .Produces<ProductListResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
