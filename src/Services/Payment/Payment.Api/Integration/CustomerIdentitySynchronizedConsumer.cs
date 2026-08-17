using MassTransit;
using Microservices.Contracts.Customers.V1;
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

        if (existing is null)
        {
            var identityOwner = await dbContext.PaymentCustomers.AsNoTracking().SingleOrDefaultAsync(
                customer => customer.IdentityProvider == message.IdentityProvider &&
                            customer.IdentitySubject == message.IdentitySubject,
                context.CancellationToken);
            if (identityOwner is not null && identityOwner.CustomerId != message.CustomerId)
            {
                throw new InvalidOperationException(
                    "The synchronized identity is already bound to another payment customer.");
            }

            dbContext.PaymentCustomers.Add(PaymentCustomer.Create(
                message.CustomerId,
                message.IdentityProvider,
                message.IdentitySubject,
                timeProvider.GetUtcNow()));
        }
        else
        {
            existing.EnsureIdentity(
                message.IdentityProvider,
                message.IdentitySubject,
                timeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
