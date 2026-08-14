namespace Product.Api.Features.Products.Creating.V1;

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string CurrencyCode);
