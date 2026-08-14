using Microservices.Application;

namespace Product.Api.Features.Products.Deleting.V1;

internal sealed record DeleteProductCommand(Guid ProductId, long ExpectedVersion)
    : ICommand<Result>;
