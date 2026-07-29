using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.Exporting.V1;

internal sealed class ExportCustomerHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IQueryHandler<ExportCustomerQuery, CustomerExportResponse>
{
    public async Task<CustomerExportResponse> Handle(
        ExportCustomerQuery query,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .GetRequiredByIdentityAsync(
                query.Provider,
                query.Subject,
                cancellationToken);

        return new CustomerExportResponse(
            timeProvider.GetUtcNow(),
            CustomerMappings.ToResponse(customer));
    }
}
