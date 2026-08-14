using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Product.Api.Features.Products.GettingById.V1;

internal sealed class GetProductByIdHandler(ProductDbContext dbContext)
    : IQueryHandler<GetProductByIdQuery, Result<ProductResponse>>
{
    public async Task<Result<ProductResponse>> Handle(
        GetProductByIdQuery query,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(product => product.Id == query.ProductId, cancellationToken);

        return product is null
            ? ProductApplicationErrors.ProductNotFound
            : Result.Success(ProductMappings.ToResponse(product));
    }
}
