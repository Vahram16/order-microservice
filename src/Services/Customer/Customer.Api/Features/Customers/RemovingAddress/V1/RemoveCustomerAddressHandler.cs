using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;

namespace Customer.Api.Features.Customers.RemovingAddress.V1;

internal sealed class RemoveCustomerAddressHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<RemoveCustomerAddressCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        RemoveCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.GetRequiredByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);
        customer.EnsureExpectedVersion(command.ExpectedVersion);

        var now = timeProvider.GetUtcNow();
        customer.RemoveAddress(command.AddressId, now);
        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            Domain.CustomerAuditActions.AddressRemoved,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
