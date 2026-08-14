using Microservices.Application;

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
