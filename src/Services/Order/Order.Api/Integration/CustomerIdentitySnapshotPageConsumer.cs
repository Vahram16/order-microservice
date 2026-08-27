using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Customers.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class CustomerIdentitySnapshotPageConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<SynchronizeCustomerIdentitySnapshot> commandSender,
    TimeProvider timeProvider)
    : IConsumer<CustomerIdentitySnapshotPage>
{
    private const int MaximumPageSize = 500;

    public async Task Consume(ConsumeContext<CustomerIdentitySnapshotPage> context)
    {
        var message = context.Message;
        Validate(message);

        var state = await dbContext.Set<OrderReferenceDataSynchronization>()
            .SingleOrDefaultAsync(
                item => item.Id == OrderReferenceDataSynchronization.SingletonId,
                context.CancellationToken);
        if (state is null ||
            state.SnapshotId != message.SnapshotId ||
            state.CustomerCompleted ||
            state.CustomerAfterCustomerId != message.AfterCustomerId)
        {
            return;
        }

        var customerIds = message.Items.Select(item => item.CustomerId).ToArray();
        var existingCustomers = await dbContext.OrderCustomers
            .Where(item => customerIds.Contains(item.CustomerId))
            .ToListAsync(context.CancellationToken);
        var byCustomerId = existingCustomers.ToDictionary(item => item.CustomerId);

        var identitySubjects = message.Items
            .Select(item => item.IdentitySubject)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var possibleIdentityOwners = identitySubjects.Length == 0
            ? []
            : await dbContext.OrderCustomers
                .Where(item => identitySubjects.Contains(item.IdentitySubject))
                .ToListAsync(context.CancellationToken);
        var byIdentity = possibleIdentityOwners.ToDictionary(
            item => IdentityKey(item.IdentityProvider, item.IdentitySubject),
            item => item,
            StringComparer.Ordinal);

        foreach (var item in message.Items)
        {
            if (byCustomerId.TryGetValue(item.CustomerId, out var existing))
            {
                if (!existing.Synchronize(
                        item.IdentityProvider,
                        item.IdentitySubject,
                        item.UpdatedAtUtc))
                {
                    throw new OrderWorkflowException("order.customer_identity_conflict");
                }

                continue;
            }

            var identityKey = IdentityKey(item.IdentityProvider, item.IdentitySubject);
            if (byIdentity.TryGetValue(identityKey, out var identityOwner) &&
                identityOwner.CustomerId != item.CustomerId)
            {
                throw new OrderWorkflowException("order.customer_identity_conflict");
            }

            var projection = OrderCustomerProjection.Create(
                item.CustomerId,
                item.IdentityProvider,
                item.IdentitySubject,
                item.UpdatedAtUtc);
            dbContext.OrderCustomers.Add(projection);
            byCustomerId.Add(item.CustomerId, projection);
            byIdentity[identityKey] = projection;
        }

        var now = timeProvider.GetUtcNow();
        if (!state.ApplyCustomerPage(
                message.SnapshotId,
                message.AfterCustomerId,
                message.NextAfterCustomerId,
                message.IsLastPage,
                now))
        {
            return;
        }

        if (!message.IsLastPage)
        {
            state.MarkCustomerRequested(now);
            await commandSender.SendAsync(
                new SynchronizeCustomerIdentitySnapshot(
                    state.SnapshotId,
                    state.CustomerAfterCustomerId,
                    MaximumPageSize),
                new IntegrationMessageMetadata(CorrelationId: state.SnapshotId),
                context.CancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new OrderReferenceDataSynchronizationException(
                "order.customer_snapshot_concurrency",
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw new OrderReferenceDataSynchronizationException(
                "order.customer_snapshot_persistence",
                exception);
        }
    }

    private static void Validate(CustomerIdentitySnapshotPage message)
    {
        if (message.SnapshotId == Guid.Empty ||
            message.Items.Count > MaximumPageSize ||
            message.IsLastPage != (message.NextAfterCustomerId is null) ||
            (!message.IsLastPage && message.Items.Count == 0) ||
            (!message.IsLastPage && message.Items[^1].CustomerId != message.NextAfterCustomerId) ||
            message.Items.Any(item =>
                item.CustomerId == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.IdentityProvider) ||
                item.IdentityProvider.Length > 32 ||
                string.IsNullOrWhiteSpace(item.IdentitySubject) ||
                item.IdentitySubject.Length > 255) ||
            message.Items.Select(item => item.CustomerId).Distinct().Count() != message.Items.Count ||
            message.Items
                .Select(item => IdentityKey(item.IdentityProvider, item.IdentitySubject))
                .Distinct(StringComparer.Ordinal)
                .Count() != message.Items.Count)
        {
            throw new OrderWorkflowException("order.invalid_customer_snapshot_page");
        }
    }

    private static string IdentityKey(string provider, string subject) =>
        string.Concat(provider, "\u001f", subject);
}
