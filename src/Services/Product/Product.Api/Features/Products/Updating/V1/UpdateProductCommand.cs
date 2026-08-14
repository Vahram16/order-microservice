using Microservices.Application;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.Updating.V1;

internal sealed record UpdateProductCommand(
    Guid ProductId,
    long ExpectedVersion,
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string CurrencyCode)
    : ICommand<Result<ProductResponse>>;
