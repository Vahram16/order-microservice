using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.UpdatingAddress.V1;

internal sealed class UpdateCustomerAddressHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateCustomerAddressCommand, Result<CustomerResponse>>
{
    public async Task<Result<CustomerResponse>> Handle(
        UpdateCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ExecuteOnceAsync(command, cancellationToken));
    }

    private async Task<Result<CustomerResponse>> ExecuteOnceAsync(
        UpdateCustomerAddressCommand command,
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
        var update = customer.UpdateAddress(command.AddressId, command.Address, now);
        if (update.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return update.Error;
        }

        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            CustomerAuditActions.AddressUpdated,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(CustomerMappings.ToResponse(customer));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CustomerErrors.VersionMismatch;
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
}
