using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.Exporting.V1;

internal sealed class ExportCustomerHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IQueryHandler<ExportCustomerQuery, Result<CustomerExportResponse>>
{
    public async Task<Result<CustomerExportResponse>> Handle(
        ExportCustomerQuery query,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .FindByIdentityAsync(
                query.Provider,
                query.Subject,
                cancellationToken);

        return customer is null
            ? CustomerApplicationErrors.CustomerNotFound
            : Result.Success(new CustomerExportResponse(
                timeProvider.GetUtcNow(),
                CustomerMappings.ToResponse(customer)));
    }
}
