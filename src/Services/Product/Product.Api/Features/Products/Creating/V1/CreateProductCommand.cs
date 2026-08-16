using Microservices.Application;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.Creating.V1;

internal sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string CurrencyCode)
    : ICommand<Result<ProductResponse>>;
