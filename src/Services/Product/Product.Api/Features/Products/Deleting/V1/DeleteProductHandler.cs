using Microservices.Application;
using Microservices.Application.Messaging;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Product.Api.Features.Products.Common;
using Product.Api.Persistence;

namespace Product.Api.Features.Products.Deleting.V1;

internal sealed class DeleteProductHandler(
    ProductDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider)
    : ICommandHandler<DeleteProductCommand, Result>
{
    public async Task<Result> Handle(
        DeleteProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(
            product => product.Id == command.ProductId,
            cancellationToken);
        if (product is null)
        {
            return ProductApplicationErrors.ProductNotFound;
        }

        var version = product.EnsureExpectedVersion(command.ExpectedVersion);
        if (version.IsFailure)
        {
            return version.Error;
        }

        var now = timeProvider.GetUtcNow();
        await eventPublisher.PublishAsync(
            new ProductCatalogChanged(
                product.Id,
                product.Sku,
                product.Name,
                product.Price,
                product.CurrencyCode,
                product.Version,
                IsAvailable: false,
                now),
            cancellationToken: cancellationToken);
        dbContext.Products.Remove(product);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Domain.ProductErrors.VersionMismatch;
        }

        return Result.Success();
    }
}
