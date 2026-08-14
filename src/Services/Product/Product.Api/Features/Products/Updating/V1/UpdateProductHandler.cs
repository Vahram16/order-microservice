using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Product.Api.Features.Products.Updating.V1;

internal sealed class UpdateProductHandler(
    ProductDbContext dbContext,
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

        var update = product.Update(
            command.Sku,
            command.Name,
            command.Description,
            command.Price,
            command.CurrencyCode,
            timeProvider.GetUtcNow());
        if (update.IsFailure)
        {
            return update.Error;
        }

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
