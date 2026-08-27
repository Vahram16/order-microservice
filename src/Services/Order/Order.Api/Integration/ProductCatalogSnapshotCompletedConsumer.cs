using MassTransit;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class ProductCatalogSnapshotCompletedConsumer(OrderDbContext dbContext) : IConsumer<ProductCatalogSnapshotCompleted>
{
    public async Task Consume(ConsumeContext<ProductCatalogSnapshotCompleted> context)
    {
        var state = await dbContext.Set<OrderReferenceDataSynchronization>().SingleOrDefaultAsync(item => item.Id == OrderReferenceDataSynchronization.SingletonId, context.CancellationToken);
        if (state is null || !state.MarkProductCompleted(context.Message.SnapshotId, context.Message.OccurredAtUtc)) return;
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
