namespace Product.Api.Features.Products.Common;

public sealed record ProductListResponse(
    IReadOnlyList<ProductResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
