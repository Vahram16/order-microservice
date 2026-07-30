using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.RemovingAddress.V1;

internal sealed class RemoveCustomerAddressHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<RemoveCustomerAddressCommand, Result<CustomerResponse>>
{
    public async Task<Result<CustomerResponse>> Handle(
        RemoveCustomerAddressCommand command,
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

        var now = timeProvider.GetUtcNow();
        var remove = customer.RemoveAddress(command.AddressId, now);
        if (remove.IsFailure)
        {
            return remove.Error;
        }

        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            CustomerAuditActions.AddressRemoved,
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
