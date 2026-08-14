namespace Product.Api.Features.Products.Common;

internal static class ProductErrorCatalog
{
    private static readonly Dictionary<string, ProductProblemDescriptor> Descriptors =
        new(StringComparer.Ordinal)
        {
            ["product.not_found"] = new(
                "product.not_found",
                "Product not found",
                ErrorCategory.MissingResource,
                "No product exists for the requested identifier.",
                false),
            ["product.sku_conflict"] = new(
                "product.sku_conflict",
                "Product SKU conflict",
                ErrorCategory.StateConflict,
                "Another product already uses the requested SKU.",
                false),
            ["product.validation"] = new(
                "product.validation",
                "Product validation failed",
                ErrorCategory.InvalidInput,
                "A product-domain value failed invariant validation.",
                false),
            ["product.invalid_price"] = new(
                "product.invalid_price",
                "Invalid product price",
                ErrorCategory.InvalidInput,
                "The product price is outside the supported range or precision.",
                false),
            ["product.invalid_currency_code"] = new(
                "product.invalid_currency_code",
                "Invalid currency code",
                ErrorCategory.InvalidInput,
                "The currency code is not a three-letter ASCII value.",
                false),
            ["product.version_mismatch"] = new(
                "product.version_mismatch",
                "Product version mismatch",
                ErrorCategory.ConcurrencyConflict,
                "The product changed after the supplied ETag was issued.",
                true),
            ["product.precondition_required"] = new(
                "product.precondition_required",
                "Precondition required",
                ErrorCategory.PreconditionRequired,
                "A current strong product ETag is required for this operation.",
                false),
            ["product.invalid_precondition"] = new(
                "product.invalid_precondition",
                "Invalid precondition",
                ErrorCategory.InvalidInput,
                "The If-Match header is not a valid strong product ETag.",
                false)
        };

    internal static ProductProblemDescriptor GetRequired(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!Descriptors.TryGetValue(error.Code, out var descriptor))
        {
            throw new InvalidOperationException(
                $"Error code '{error.Code}' is not registered in the Product error catalog.");
        }

        if (descriptor.Category != error.Category)
        {
            throw new InvalidOperationException(
                $"Error code '{error.Code}' uses category '{error.Category}', but the catalog requires '{descriptor.Category}'.");
        }

        return descriptor;
    }

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(
                "/errors/v1/product/{code}",
                IResult (string code) =>
                {
                    if (!Descriptors.TryGetValue(code, out var descriptor))
                    {
                        return Results.NotFound();
                    }

                    return Results.Ok(new
                    {
                        type = descriptor.Type,
                        descriptor.Code,
                        descriptor.Title,
                        descriptor.Status,
                        description = descriptor.Description,
                        descriptor.Retryable
                    });
                })
            .WithName("GetProductErrorDescriptionV1")
            .WithSummary("Describes a stable version 1 Product API Problem Details type.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();
    }
}
