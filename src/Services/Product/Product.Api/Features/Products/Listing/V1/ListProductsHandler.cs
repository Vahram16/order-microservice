using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Product.Api.Features.Products.Listing.V1;

internal sealed class ListProductsHandler(ProductDbContext dbContext)
    : IQueryHandler<ListProductsQuery, ProductListResponse>
{
    public async Task<ProductListResponse> Handle(
        ListProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking();
        var totalCount = await products.LongCountAsync(cancellationToken);
        var offset = checked((query.Page - 1) * query.PageSize);
        var items = await products
            .OrderBy(product => product.Sku)
            .ThenBy(product => product.Id)
            .Skip(offset)
            .Take(query.PageSize)
            .Select(product => new ProductResponse(
                product.Id,
                product.Sku,
                product.Name,
                product.Description,
                product.Price,
                product.CurrencyCode,
                product.CreatedAt,
                product.UpdatedAt,
                product.Version))
            .ToArrayAsync(cancellationToken);

        return new ProductListResponse(items, query.Page, query.PageSize, totalCount);
    }
}
