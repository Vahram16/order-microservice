using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Product.Api.Features.Products.Creating.V1;

internal sealed class CreateProductHandler(
    ProductDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<CreateProductCommand, Result<ProductResponse>>
{
    public async Task<Result<ProductResponse>> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var creation = Domain.Product.Create(
            command.Sku,
            command.Name,
            command.Description,
            command.Price,
            command.CurrencyCode,
            timeProvider.GetUtcNow());
        if (creation.IsFailure)
        {
            return creation.Error;
        }

        var product = creation.Value;
        dbContext.Products.Add(product);
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
