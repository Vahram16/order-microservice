using Microservices.Application;
using Microsoft.EntityFrameworkCore;
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

        var operation = await dbContext.PaymentMethodSetupOperations
            .SingleOrDefaultAsync(item => item.Id == command.RequestId, cancellationToken);

        if (operation is not null && operation.PaymentCustomerId != customer.Id)
        {
            return PaymentApplicationErrors.IdempotencyKeyReused;
        }

        if (customer.ProviderCustomerId is null)
        {
            var providerCustomerResult = await EnsureProviderCustomerAsync(customer, cancellationToken);
            if (providerCustomerResult.IsFailure)
            {
                return providerCustomerResult.Error;
            }
        }

        operation ??= PaymentMethodSetupOperation.Create(
            command.RequestId,
            customer.Id,
            timeProvider.GetUtcNow());

        if (dbContext.Entry(operation).State == EntityState.Detached)
        {
            dbContext.PaymentMethodSetupOperations.Add(operation);
            await dbContext.SaveChangesAsync(cancellationToken);
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

    private async Task<Result> EnsureProviderCustomerAsync(
        Domain.PaymentCustomer customer,
        CancellationToken cancellationToken)
    {
        string providerCustomerId;
        try
        {
            providerCustomerId = await paymentProvider.CreateCustomerAsync(
                customer.Id,
                customer.CustomerId,
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
