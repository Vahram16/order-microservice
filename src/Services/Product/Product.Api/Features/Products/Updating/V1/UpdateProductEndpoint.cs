using MediatR;

namespace Product.Api.Features.Products.Updating.V1;

internal static class UpdateProductEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut(
                "/{productId:guid}",
                async (
                    Guid productId,
                    UpdateProductRequest request,
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
                        new UpdateProductCommand(
                            productId,
                            expectedVersion.Value,
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
                            return Results.Ok(product);
                        },
                        error => ProductHttpResults.Problem(error, httpContext));
                })
            .WithName("UpdateProduct")
            .WithSummary("Replaces a product using optimistic concurrency.")
            .RequireAuthorization()
            .Produces<ProductResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
