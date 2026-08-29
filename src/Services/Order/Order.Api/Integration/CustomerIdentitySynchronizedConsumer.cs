using MassTransit;
using Microservices.Contracts.Customers.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class CustomerIdentitySynchronizedConsumer(OrderDbContext dbContext)
    : IConsumer<CustomerIdentitySynchronized>
{
    public async Task Consume(ConsumeContext<CustomerIdentitySynchronized> context)
    {
        var message = context.Message;
        var existing = await dbContext.OrderCustomers.SingleOrDefaultAsync(
            item => item.CustomerId == message.CustomerId,
            context.CancellationToken);
        if (existing is not null)
        {
            if (!existing.Matches(message.IdentityProvider, message.IdentitySubject))
            {
                throw new OrderWorkflowException("order.customer_identity_conflict");
            }

            return;
        }

        var identityOwner = await dbContext.OrderCustomers.SingleOrDefaultAsync(
            item => item.IdentityProvider == message.IdentityProvider && item.IdentitySubject == message.IdentitySubject,
            context.CancellationToken);
        if (identityOwner is not null)
        {
            throw new OrderWorkflowException("order.customer_identity_conflict");
        }

        dbContext.OrderCustomers.Add(OrderCustomerProjection.Create(
            message.CustomerId,
            message.IdentityProvider,
            message.IdentitySubject,
            message.OccurredAtUtc));
        try
        {
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(OrderDatabaseConstraints.CustomerIdentity))
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.OrderCustomers.SingleOrDefaultAsync(
                item => item.CustomerId == message.CustomerId,
                context.CancellationToken);
            if (current is null || !current.Matches(message.IdentityProvider, message.IdentitySubject))
            {
                throw new OrderWorkflowException("order.customer_identity_conflict");
            }
        }
    }
}
