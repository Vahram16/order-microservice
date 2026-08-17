using MassTransit;
using Microservices.Contracts.Customers.V1;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class CustomerIdentitySynchronizedConsumer(
    PaymentDbContext dbContext,
    TimeProvider timeProvider) : IConsumer<CustomerIdentitySynchronized>
{
    public async Task Consume(ConsumeContext<CustomerIdentitySynchronized> context)
    {
        var message = context.Message;
        var existing = await dbContext.PaymentCustomers.SingleOrDefaultAsync(
            customer => customer.CustomerId == message.CustomerId,
            context.CancellationToken);

        if (existing is not null)
        {
            var consistency = existing.EnsureCustomerIdentity(
                message.CustomerId,
                message.IdentityProvider,
                message.IdentitySubject);
            if (consistency.IsFailure)
            {
                throw new CustomerIdentitySynchronizationException(consistency.Error.Code);
            }

            return;
        }

        var identityOwner = await dbContext.PaymentCustomers.SingleOrDefaultAsync(
            customer =>
                customer.IdentityProvider == message.IdentityProvider &&
                customer.IdentitySubject == message.IdentitySubject,
            context.CancellationToken);

        if (identityOwner is not null)
        {
            throw new CustomerIdentitySynchronizationException(
                PaymentErrors.CustomerIdentityConflict.Code);
        }

        var created = PaymentCustomer.Create(
            Guid.NewGuid(),
            message.CustomerId,
            message.IdentityProvider,
            message.IdentitySubject,
            timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            throw new CustomerIdentitySynchronizationException(created.Error.Code);
        }

        dbContext.PaymentCustomers.Add(created.Value);
        try
        {
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(PaymentDatabaseConstraints.CustomerId) ||
            exception.IsUniqueConstraintViolation(PaymentDatabaseConstraints.CustomerIdentity))
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.PaymentCustomers.SingleOrDefaultAsync(
                customer => customer.CustomerId == message.CustomerId,
                context.CancellationToken);

            if (current is null ||
                current.EnsureCustomerIdentity(
                    message.CustomerId,
                    message.IdentityProvider,
                    message.IdentitySubject).IsFailure)
            {
                throw new CustomerIdentitySynchronizationException(
                    PaymentErrors.CustomerIdentityConflict.Code);
            }
        }
    }

    private sealed class CustomerIdentitySynchronizationException(string code)
        : Exception(code), IPermanentConsumerFailure;
}
