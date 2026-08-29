using Microservices.Application;
using Microservices.Application.Messaging;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Product.Api.Features.Products.Common;
using Product.Api.Persistence;

namespace Product.Api.Features.Products.Updating.V1;

internal sealed class UpdateProductHandler(
    ProductDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateProductCommand, Result<ProductResponse>>
{
    public async Task<Result<ProductResponse>> Handle(
        UpdateProductCommand command,
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
        var update = product.Update(
            command.Sku,
            command.Name,
            command.Description,
            command.Price,
            command.CurrencyCode,
            now);
        if (update.IsFailure)
        {
            return update.Error;
        }

        await eventPublisher.PublishAsync(
            new ProductCatalogChanged(
                product.Id,
                product.Sku,
                product.Name,
                product.Price,
                product.CurrencyCode,
                product.Version,
                IsAvailable: true,
                now),
            cancellationToken: cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Domain.ProductErrors.VersionMismatch;
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(ProductDatabaseConstraints.Sku))
        {
            return ProductApplicationErrors.SkuConflict;
        }

        return Result.Success(ProductMappings.ToResponse(product));
    }
}
