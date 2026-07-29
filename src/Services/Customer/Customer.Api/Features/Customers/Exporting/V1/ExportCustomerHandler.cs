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
            .Include(entity => entity.Addresses)
            .SingleOrDefaultAsync(
                entity =>
                    entity.IdentityProvider == query.Provider &&
                    entity.IdentitySubject == query.Subject,
                cancellationToken)
            ?? throw new Domain.CustomerNotFoundException();

        return new CustomerExportResponse(
            timeProvider.GetUtcNow(),
            CustomerMappings.ToResponse(customer));
    }
}
