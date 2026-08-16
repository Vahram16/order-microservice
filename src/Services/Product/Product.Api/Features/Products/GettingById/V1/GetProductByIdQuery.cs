using Microservices.Application;
using Product.Api.Features.Products.Common;

namespace Product.Api.Features.Products.GettingById.V1;

internal sealed record GetProductByIdQuery(Guid ProductId)
    : IQuery<Result<ProductResponse>>;
