using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal sealed class CloseCustomerAccountHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<CloseCustomerAccountCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        CloseCustomerAccountCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.GetRequiredByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);

        if (customer.Status == Domain.CustomerStatus.Deactivated)
        {
            return CustomerMappings.ToResponse(customer);
        }

        customer.EnsureExpectedVersion(command.ExpectedVersion);
        var now = timeProvider.GetUtcNow();
        customer.CloseAccount(now);
        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            Domain.CustomerAuditActions.AccountClosed,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
