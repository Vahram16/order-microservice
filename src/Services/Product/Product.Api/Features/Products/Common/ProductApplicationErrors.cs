namespace Product.Api.Features.Products.Common;

internal static class ProductApplicationErrors
{
    public static OperationError ProductNotFound { get; } = OperationError.MissingResource(
        "product.not_found",
        "Product was not found.");

    public static OperationError SkuConflict { get; } = OperationError.StateConflict(
        "product.sku_conflict",
        "A product with the same SKU already exists.");

    public static OperationError PreconditionRequired { get; } = OperationError.PreconditionRequired(
        "product.precondition_required",
        "An If-Match header containing the current product ETag is required.");

    public static OperationError InvalidPrecondition { get; } = OperationError.InvalidInput(
        "product.invalid_precondition",
        "If-Match must contain exactly one strong product ETag.");
}
