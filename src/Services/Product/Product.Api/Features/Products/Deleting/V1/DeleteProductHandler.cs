using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Product.Api.Features.Products.Deleting.V1;

internal sealed class DeleteProductHandler(ProductDbContext dbContext)
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
