namespace Product.Api.Features.Products.Updating.V1;

public sealed record UpdateProductRequest(
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string CurrencyCode);
