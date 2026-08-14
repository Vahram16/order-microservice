using MediatR;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.Creating.V1;

internal static class CreateProductEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost(
                "/",
                async (
                    CreateProductRequest request,
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new CreateProductCommand(
                            request.Sku,
                            request.Name,
                            request.Description,
                            request.Price,
                            request.CurrencyCode),
                        cancellationToken);

                    return result.Match<IResult>(
                        product =>
                        {
                            ProductHttp.WriteEtag(httpContext.Response, product.Id, product.Version);
                            return Results.Created($"/api/v1/products/{product.Id}", product);
                        },
                        error => ProductHttpResults.Problem(error, httpContext));
                })
            .WithName("CreateProduct")
            .WithSummary("Creates a product.")
            .RequireAuthorization()
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
