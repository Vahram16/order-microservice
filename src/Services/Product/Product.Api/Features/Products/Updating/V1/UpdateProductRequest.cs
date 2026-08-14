using System.Text.Json.Serialization;

namespace Product.Api.Features.Products.Updating.V1;

public sealed record UpdateProductRequest(
    string Sku,
    string Name,
    string? Description,
    [property: JsonRequired] decimal Price,
    string CurrencyCode);
