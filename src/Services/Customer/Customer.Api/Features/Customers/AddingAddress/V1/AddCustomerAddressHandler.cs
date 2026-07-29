using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal sealed class AddCustomerAddressHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<AddCustomerAddressCommand, AddCustomerAddressResult>
{
    public async Task<AddCustomerAddressResult> Handle(
        AddCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ExecuteOnceAsync(command, cancellationToken));
    }

    private async Task<AddCustomerAddressResult> ExecuteOnceAsync(
        AddCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            command.Provider,
            command.Subject,
            cancellationToken);

        var existing = customer.FindAddress(command.AddressId);
        if (existing is not null)
        {
            if (!existing.Matches(command.Address))
            {
                throw new CustomerIdempotencyConflictException(command.AddressId);
            }

            return new AddCustomerAddressResult(
                CustomerMappings.ToResponse(customer),
                existing.Id,
                false);
        }

        customer.EnsureExpectedVersion(command.ExpectedVersion);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await CustomerPersistence.ClearCompetingDefaultsAsync(
            dbContext,
            customer.Id,
            command.AddressId,
            command.Address,
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        customer.AddAddress(command.AddressId, command.Address, now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            command.Subject,
            CustomerAuditActions.AddressAdded,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AddCustomerAddressResult(
                CustomerMappings.ToResponse(customer),
                command.AddressId,
                true);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReloadIdempotentResultAsync(command, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            CustomerPersistence.IsUniqueConstraintViolation(
                exception,
                CustomerConstraintNames.AddressPrimaryKey))
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReloadIdempotentResultAsync(command, cancellationToken);
        }
    }

    private async Task<AddCustomerAddressResult> ReloadIdempotentResultAsync(
        AddCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            command.Provider,
            command.Subject,
            cancellationToken);
        var address = customer.FindAddress(command.AddressId);
        if (address is null || !address.Matches(command.Address))
        {
            throw new CustomerVersionMismatchException(
                command.ExpectedVersion,
                customer.Version);
        }

        return new AddCustomerAddressResult(
            CustomerMappings.ToResponse(customer),
            address.Id,
            false);
    }
}
