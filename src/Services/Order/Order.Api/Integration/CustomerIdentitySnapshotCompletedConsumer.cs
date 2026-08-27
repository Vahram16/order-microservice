using MassTransit;
using Microservices.Contracts.Customers.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class CustomerIdentitySnapshotCompletedConsumer(OrderDbContext dbContext) : IConsumer<CustomerIdentitySnapshotCompleted>
{
    public async Task Consume(ConsumeContext<CustomerIdentitySnapshotCompleted> context)
    {
        var state = await dbContext.Set<OrderReferenceDataSynchronization>().SingleOrDefaultAsync(item => item.Id == OrderReferenceDataSynchronization.SingletonId, context.CancellationToken);
        if (state is null || !state.MarkCustomerCompleted(context.Message.SnapshotId, context.Message.OccurredAtUtc)) return;
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
