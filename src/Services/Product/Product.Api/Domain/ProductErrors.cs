using Microservices.Primitives;

namespace Product.Api.Domain;

public static class ProductErrors
{
    public static OperationError Validation(string field, string description) => OperationError.InvalidInput(
        "product.validation",
        description,
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = field
        });

    public static OperationError InvalidPrice { get; } = OperationError.InvalidInput(
        "product.invalid_price",
        "Price must be non-negative, cannot exceed 9999999999999999.99, and can have at most two decimal places.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = "price"
        });

    public static OperationError InvalidCurrencyCode { get; } = OperationError.InvalidInput(
        "product.invalid_currency_code",
        "Currency code must contain exactly three ASCII letters.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = "currencyCode"
        });

    public static OperationError VersionMismatch { get; } = OperationError.ConcurrencyConflict(
        "product.version_mismatch",
        "The product changed after the supplied version was issued. Reload the product and retry.");
}
