using Microservices.Application;

namespace Product.Api.Features.Products.Listing.V1;

internal sealed record ListProductsQuery(int Page = 1, int PageSize = 20)
    : IQuery<ProductListResponse>;
