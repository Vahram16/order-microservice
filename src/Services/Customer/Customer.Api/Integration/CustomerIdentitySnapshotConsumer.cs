using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Customers.V1;
using Customer.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Integration;

internal sealed class CustomerIdentitySnapshotConsumer(
    CustomerDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    IIntegrationCommandSender<SynchronizeCustomerIdentitySnapshot> commandSender,
    TimeProvider timeProvider) : IConsumer<SynchronizeCustomerIdentitySnapshot>
{
    private const int MaximumPageSize = 500;

    public async Task Consume(ConsumeContext<SynchronizeCustomerIdentitySnapshot> context)
    {
        var message = context.Message;
        if (message.SnapshotId == Guid.Empty || message.PageSize is <= 0 or > MaximumPageSize)
            throw new CustomerSnapshotException("customer.snapshot.invalid_request");

        var query = dbContext.Customers.AsNoTracking().OrderBy(customer => customer.Id);
        if (message.AfterCustomerId is { } afterCustomerId)
            query = (IOrderedQueryable<Domain.Customer>)query.Where(customer => customer.Id.CompareTo(afterCustomerId) > 0).OrderBy(customer => customer.Id);

        var page = await query.Take(message.PageSize + 1).ToListAsync(context.CancellationToken);
        var current = page.Take(message.PageSize).ToArray();
        foreach (var customer in current)
        {
            await eventPublisher.PublishAsync(
                new CustomerIdentitySynchronized(customer.Id, customer.IdentityProvider, customer.IdentitySubject, customer.UpdatedAt),
                cancellationToken: context.CancellationToken);
        }

        if (page.Count > message.PageSize)
        {
            await commandSender.SendAsync(
                new SynchronizeCustomerIdentitySnapshot(message.SnapshotId, current[^1].Id, message.PageSize),
                new IntegrationMessageMetadata(CorrelationId: message.SnapshotId),
                context.CancellationToken);
            return;
        }

        await eventPublisher.PublishAsync(
            new CustomerIdentitySnapshotCompleted(message.SnapshotId, timeProvider.GetUtcNow()),
            new IntegrationMessageMetadata(CorrelationId: message.SnapshotId),
            context.CancellationToken);
    }

    private sealed class CustomerSnapshotException(string code) : Exception(code), Microservices.Messaging.IPermanentConsumerFailure;
}
