using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal sealed class AddCustomerAddressHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<AddCustomerAddressCommand, Result<AddCustomerAddressResult>>
{
    public async Task<Result<AddCustomerAddressResult>> Handle(
        AddCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ExecuteOnceAsync(command, cancellationToken));
    }

    private async Task<Result<AddCustomerAddressResult>> ExecuteOnceAsync(
        AddCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await dbContext.Customers.FindByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);
        if (customer is null)
        {
            return CustomerApplicationErrors.CustomerNotFound;
        }

        var existing = customer.FindAddress(command.AddressId);
        if (existing is not null)
        {
            var matches = existing.Matches(command.Address);
            if (matches.IsFailure)
            {
                return matches.Error;
            }

            return matches.Value
                ? Result.Success(new AddCustomerAddressResult(
                    CustomerMappings.ToResponse(customer),
                    existing.Id,
                    false))
                : CustomerApplicationErrors.IdempotencyKeyReused;
        }

        var version = customer.EnsureExpectedVersion(command.ExpectedVersion);
        if (version.IsFailure)
        {
            return version.Error;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ClearCompetingAddressDefaultsAsync(
            customer.Id,
            command.AddressId,
            command.Address.IsDefaultShipping,
            command.Address.IsDefaultBilling,
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        var add = customer.AddAddress(command.AddressId, command.Address, now);
        if (add.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CustomerApplicationErrors.TranslateDomain(add.Error);
        }

        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            CustomerAuditActions.AddressAdded,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(new AddCustomerAddressResult(
                CustomerMappings.ToResponse(customer),
                add.Value.Id,
                true));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReloadIdempotentResultAsync(command, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(CustomerDatabaseConstraints.AddressPrimaryKey))
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReloadIdempotentResultAsync(command, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(CustomerDatabaseConstraints.DefaultShipping))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CustomerApplicationErrors.DefaultShippingConflict;
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(CustomerDatabaseConstraints.DefaultBilling))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CustomerApplicationErrors.DefaultBillingConflict;
        }
    }

    private async Task<Result<AddCustomerAddressResult>> ReloadIdempotentResultAsync(
        AddCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await dbContext.Customers.FindByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);
        if (customer is null)
        {
            return CustomerApplicationErrors.CustomerNotFound;
        }

        var address = customer.FindAddress(command.AddressId);
        if (address is null)
        {
            return CustomerErrors.VersionMismatch;
        }

        var matches = address.Matches(command.Address);
        if (matches.IsFailure)
        {
            return matches.Error;
        }

        return matches.Value
            ? Result.Success(new AddCustomerAddressResult(
                CustomerMappings.ToResponse(customer),
                address.Id,
                false))
            : CustomerApplicationErrors.IdempotencyKeyReused;
    }
}
