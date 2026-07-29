using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal sealed class CloseCustomerAccountHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<CloseCustomerAccountCommand, Result<CustomerResponse>>
{
    public async Task<Result<CustomerResponse>> Handle(
        CloseCustomerAccountCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.FindByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);
        if (customer is null)
        {
            return CustomerApplicationErrors.CustomerNotFound;
        }

        var version = customer.EnsureExpectedVersion(command.ExpectedVersion);
        if (version.IsFailure)
        {
            return version.Error;
        }

        if (customer.Status == CustomerStatus.Deactivated)
        {
            return Result.Success(CustomerMappings.ToResponse(customer));
        }

        var now = timeProvider.GetUtcNow();
        var close = customer.CloseAccount(now);
        if (close.IsFailure)
        {
            return close.Error;
        }

        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            CustomerAuditActions.AccountClosed,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerErrors.VersionMismatch;
        }

        return Result.Success(CustomerMappings.ToResponse(customer));
    }
}
