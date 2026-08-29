using Microservices.Application;
using Microsoft.EntityFrameworkCore;
using Order.Api.Features.Orders.Common;
using Order.Api.Persistence;

namespace Order.Api.Features.Orders.GettingById.V1;

internal sealed class GetOrderByIdHandler(OrderDbContext dbContext)
    : IQueryHandler<GetOrderByIdQuery, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        var customerId = await dbContext.OrderCustomers
            .Where(item => item.IdentityProvider == query.IdentityProvider && item.IdentitySubject == query.IdentitySubject)
            .Select(item => (Guid?)item.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (customerId is null)
        {
            return OrderApplicationErrors.OrderNotFound;
        }

        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == query.OrderId && item.CustomerId == customerId.Value, cancellationToken);
        return order is null
            ? OrderApplicationErrors.OrderNotFound
            : Result.Success(OrderMappings.ToResponse(order));
    }
}
