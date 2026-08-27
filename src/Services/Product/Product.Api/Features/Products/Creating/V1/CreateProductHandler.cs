using Microservices.Application;
using Microservices.Application.Messaging;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Product.Api.Features.Products.Common;
using Product.Api.Persistence;

namespace Product.Api.Features.Products.Creating.V1;

internal sealed class CreateProductHandler(
    ProductDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider)
    : ICommandHandler<CreateProductCommand, Result<ProductResponse>>
{
    public async Task<Result<ProductResponse>> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var creation = Domain.Product.Create(
            command.Sku,
            command.Name,
            command.Description,
            command.Price,
            command.CurrencyCode,
            now);
        if (creation.IsFailure)
        {
            return creation.Error;
        }

        var product = creation.Value;
        dbContext.Products.Add(product);
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
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(ProductDatabaseConstraints.Sku))
        {
            return ProductApplicationErrors.SkuConflict;
        }

        return Result.Success(ProductMappings.ToResponse(product));
    }
}
