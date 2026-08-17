using Microservices.Application;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal sealed class CreatePaymentMethodSetupHandler(
    PaymentDbContext dbContext,
    IPaymentProvider paymentProvider,
    TimeProvider timeProvider)
    : ICommandHandler<CreatePaymentMethodSetupCommand, Result<CreatePaymentMethodSetupResult>>
{
    public async Task<Result<CreatePaymentMethodSetupResult>> Handle(
        CreatePaymentMethodSetupCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.PaymentCustomers.FindByIdentityAsync(
            command.Identity.Provider,
            command.Identity.Subject,
            cancellationToken);

        if (customer is null)
        {
            return PaymentApplicationErrors.CustomerNotSynchronized;
        }

        var operationResult = await GetOrCreateSetupOperationAsync(
            command.RequestId,
            customer.Id,
            cancellationToken);
        if (operationResult.IsFailure)
        {
            return operationResult.Error;
        }

        var operation = operationResult.Value;

        if (customer.ProviderCustomerId is null)
        {
            var providerCustomerResult = await EnsureProviderCustomerAsync(customer, cancellationToken);
            if (providerCustomerResult.IsFailure)
            {
                return providerCustomerResult.Error;
            }
        }

        PaymentMethodSetupSession setup;
        try
        {
            setup = operation.ProviderSetupIntentId is null
                ? await paymentProvider.CreatePaymentMethodSetupAsync(
                    customer.Id,
                    customer.ProviderCustomerId!,
                    PaymentProviderIdempotencyKeys.PaymentMethodSetup(operation.Id),
                    cancellationToken)
                : await paymentProvider.GetPaymentMethodSetupAsync(
                    operation.ProviderSetupIntentId,
                    cancellationToken);
        }
        catch (PaymentProviderException)
        {
            return PaymentApplicationErrors.ProviderUnavailable;
        }

        if (!operation.TryAssignProviderSetupIntent(
                setup.ProviderSetupIntentId,
                timeProvider.GetUtcNow()))
        {
            return PaymentApplicationErrors.IdempotencyKeyReused;
        }

        if (string.IsNullOrWhiteSpace(setup.ClientSecret))
        {
            return PaymentApplicationErrors.ProviderUnavailable;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatePaymentMethodSetupResult(
            setup.ProviderSetupIntentId,
            setup.ClientSecret,
            setup.Status));
    }

    private async Task<Result<PaymentMethodSetupOperation>> GetOrCreateSetupOperationAsync(
        Guid requestId,
        Guid paymentCustomerId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.PaymentMethodSetupOperations
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (existing is not null)
        {
            return existing.PaymentCustomerId == paymentCustomerId
                ? Result.Success(existing)
                : PaymentApplicationErrors.IdempotencyKeyReused;
        }

        var created = PaymentMethodSetupOperation.Create(
            requestId,
            paymentCustomerId,
            timeProvider.GetUtcNow());
        dbContext.PaymentMethodSetupOperations.Add(created);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(created);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(
                PaymentDatabaseConstraints.PaymentMethodSetupPrimaryKey))
        {
            // Only the losing insert is invalid. Preserve the rest of the unit of work, especially
            // the tracked PaymentCustomer that may still need its Stripe customer id persisted.
            dbContext.Entry(created).State = EntityState.Detached;
            var concurrent = await dbContext.PaymentMethodSetupOperations
                .SingleAsync(item => item.Id == requestId, cancellationToken);

            return concurrent.PaymentCustomerId == paymentCustomerId
                ? Result.Success(concurrent)
                : PaymentApplicationErrors.IdempotencyKeyReused;
        }
    }

    private async Task<Result> EnsureProviderCustomerAsync(
        PaymentCustomer customer,
        CancellationToken cancellationToken)
    {
        string providerCustomerId;
        try
        {
            providerCustomerId = await paymentProvider.CreateCustomerAsync(
                customer.Id,
                PaymentProviderIdempotencyKeys.PaymentCustomer(customer.Id),
                cancellationToken);
        }
        catch (PaymentProviderException)
        {
            return PaymentApplicationErrors.ProviderUnavailable;
        }

        var assignment = customer.AssignProviderCustomer(
            providerCustomerId,
            timeProvider.GetUtcNow());
        if (assignment.IsFailure)
        {
            return assignment.Error;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(customer).State = EntityState.Detached;
            var current = await dbContext.PaymentCustomers
                .SingleAsync(item => item.Id == customer.Id, cancellationToken);

            return string.Equals(
                current.ProviderCustomerId,
                providerCustomerId,
                StringComparison.Ordinal)
                ? Result.Success()
                : PaymentApplicationErrors.ConcurrencyConflict;
        }
    }
}
