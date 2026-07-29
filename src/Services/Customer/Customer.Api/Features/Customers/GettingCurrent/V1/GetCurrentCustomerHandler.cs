using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.GettingCurrent.V1;

internal sealed class GetCurrentCustomerHandler(CustomerDbContext dbContext)
    : IQueryHandler<GetCurrentCustomerQuery, CustomerResponse?>
{
    public async Task<CustomerResponse?> Handle(
        GetCurrentCustomerQuery query,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .FindByIdentityAsync(
                query.Provider,
                query.Subject,
                cancellationToken);

        return customer is null ? null : CustomerMappings.ToResponse(customer);
    }
}
