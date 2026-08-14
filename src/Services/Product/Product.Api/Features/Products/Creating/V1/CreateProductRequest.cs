using System.Text.Json.Serialization;

namespace Product.Api.Features.Products.Creating.V1;

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    [property: JsonRequired] decimal Price,
    string CurrencyCode);
