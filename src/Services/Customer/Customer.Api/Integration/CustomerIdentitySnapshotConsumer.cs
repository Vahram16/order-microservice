using Customer.Api.Persistence;
using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Customers.V1;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Integration;

internal sealed class CustomerIdentitySnapshotConsumer(
    CustomerDbContext dbContext,
    IIntegrationEventPublisher eventPublisher)
    : IConsumer<SynchronizeCustomerIdentitySnapshot>
{
    private const int MaximumPageSize = 500;

    public async Task Consume(ConsumeContext<SynchronizeCustomerIdentitySnapshot> context)
    {
        var message = context.Message;
        if (message.SnapshotId == Guid.Empty || message.PageSize is <= 0 or > MaximumPageSize)
        {
            throw new CustomerSnapshotException("customer.snapshot.invalid_request");
        }

        IQueryable<Domain.Customer> query = dbContext.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Id);

        if (message.AfterCustomerId is { } afterCustomerId)
        {
            query = query
                .Where(customer => customer.Id.CompareTo(afterCustomerId) > 0)
                .OrderBy(customer => customer.Id);
        }

        var page = await query
            .Take(message.PageSize + 1)
            .ToListAsync(context.CancellationToken);
        var items = page
            .Take(message.PageSize)
            .Select(customer => new CustomerIdentitySnapshotItem(
                customer.Id,
                customer.IdentityProvider,
                customer.IdentitySubject,
                customer.UpdatedAt))
            .ToArray();
        var hasMore = page.Count > message.PageSize;
        var nextAfterCustomerId = hasMore ? items[^1].CustomerId : (Guid?)null;

        await eventPublisher.PublishAsync(
            new CustomerIdentitySnapshotPage(
                message.SnapshotId,
                message.AfterCustomerId,
                items,
                nextAfterCustomerId,
                IsLastPage: !hasMore),
            new IntegrationMessageMetadata(CorrelationId: message.SnapshotId),
            context.CancellationToken);
    }

    private sealed class CustomerSnapshotException(string code)
        : Exception(code), IPermanentConsumerFailure;
}

internal sealed class CustomerIdentitySnapshotConsumerDefinition
    : ConsumerDefinition<CustomerIdentitySnapshotConsumer>
{
    public CustomerIdentitySnapshotConsumerDefinition()
    {
        EndpointName = SynchronizeCustomerIdentitySnapshot.EndpointName;
    }
}
