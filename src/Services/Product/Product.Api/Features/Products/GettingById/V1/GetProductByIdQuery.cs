using Microservices.Application;

namespace Product.Api.Features.Products.GettingById.V1;

internal sealed record GetProductByIdQuery(Guid ProductId)
    : IQuery<Result<ProductResponse>>;
