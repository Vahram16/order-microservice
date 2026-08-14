namespace Product.Api.Features.Products.Common;

internal static class ProductMappings
{
    public static ProductResponse ToResponse(Domain.Product product) => new(
        product.Id,
        product.Sku,
        product.Name,
        product.Description,
        product.Price,
        product.CurrencyCode,
        product.CreatedAt,
        product.UpdatedAt,
        product.Version);
}
