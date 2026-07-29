using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.UpdatingAddress.V1;

internal sealed class UpdateCustomerAddressHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateCustomerAddressCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        UpdateCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ExecuteOnceAsync(command, cancellationToken));
    }

    private async Task<CustomerResponse> ExecuteOnceAsync(
        UpdateCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            command.Provider,
            command.Subject,
            cancellationToken);
        customer.EnsureExpectedVersion(command.ExpectedVersion);
        _ = customer.FindAddress(command.AddressId)
            ?? throw new CustomerAddressNotFoundException(command.AddressId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await CustomerPersistence.ClearCompetingDefaultsAsync(
            dbContext,
            customer.Id,
            command.AddressId,
            command.Address,
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        customer.UpdateAddress(command.AddressId, command.Address, now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            command.Subject,
            CustomerAuditActions.AddressUpdated,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
