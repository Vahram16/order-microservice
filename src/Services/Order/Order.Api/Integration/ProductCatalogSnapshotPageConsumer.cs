using System.Data;
using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class ProductCatalogSnapshotPageConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<SynchronizeProductCatalogSnapshot> commandSender,
    TimeProvider timeProvider)
    : IConsumer<ProductCatalogSnapshotPage>
{
    private const int MaximumPageSize = 500;
    private const int ContinuationPageSize = 200;

    public async Task Consume(ConsumeContext<ProductCatalogSnapshotPage> context)
    {
        Validate(context.Message);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(() => ConsumeCoreAsync(context));
    }

    private async Task ConsumeCoreAsync(ConsumeContext<ProductCatalogSnapshotPage> context)
    {
        var message = context.Message;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            context.CancellationToken);

        var state = await dbContext.Set<OrderReferenceDataSynchronization>()
            .SingleOrDefaultAsync(
                item => item.Id == OrderReferenceDataSynchronization.SingletonId,
                context.CancellationToken);
        if (state is null ||
            state.SnapshotId != message.SnapshotId ||
            state.ProductCompleted ||
            state.ProductAfterProductId != message.AfterProductId)
        {
            await transaction.CommitAsync(context.CancellationToken);
            return;
        }

        var productIds = message.Items.Select(item => item.ProductId).ToArray();
        var existingProducts = await dbContext.OrderProducts
            .Where(item => productIds.Contains(item.ProductId))
            .ToListAsync(context.CancellationToken);
        var byProductId = existingProducts.ToDictionary(item => item.ProductId);

        foreach (var item in message.Items)
        {
            var currencyCode = item.CurrencyCode.ToUpperInvariant();
            if (byProductId.TryGetValue(item.ProductId, out var existing))
            {
                existing.ObserveSnapshot(
                    message.SnapshotId,
                    item.Sku,
                    item.Name,
                    item.Price,
                    currencyCode,
                    item.Version,
                    item.UpdatedAtUtc);
                continue;
            }

            var projection = OrderProductProjection.Create(
                item.ProductId,
                item.Sku,
                item.Name,
                item.Price,
                currencyCode,
                item.Version,
                isAvailable: true,
                item.UpdatedAtUtc,
                message.SnapshotId);
            dbContext.OrderProducts.Add(projection);
            byProductId.Add(item.ProductId, projection);
        }

        var now = timeProvider.GetUtcNow();
        if (!state.ApplyProductPage(
                message.SnapshotId,
                message.AfterProductId,
                message.NextAfterProductId,
                message.IsLastPage,
                now))
        {
            await transaction.CommitAsync(context.CancellationToken);
            return;
        }

        if (!message.IsLastPage)
        {
            state.MarkProductRequested(now);
            await commandSender.SendAsync(
                new SynchronizeProductCatalogSnapshot(
                    state.SnapshotId,
                    state.ProductAfterProductId,
                    ContinuationPageSize),
                new IntegrationMessageMetadata(CorrelationId: state.SnapshotId),
                context.CancellationToken);
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        if (message.IsLastPage)
        {
            var snapshotId = message.SnapshotId;
            var cycleStartedAt = state.CycleStartedAt;
            await dbContext.OrderProducts
                .Where(item =>
                    item.IsAvailable &&
                    (item.LastSnapshotId == null || item.LastSnapshotId != snapshotId) &&
                    item.UpdatedAt <= cycleStartedAt)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.IsAvailable, false)
                        .SetProperty(item => item.UpdatedAt, now),
                    context.CancellationToken);
        }

        await transaction.CommitAsync(context.CancellationToken);
    }

    private static void Validate(ProductCatalogSnapshotPage message)
    {
        if (message.SnapshotId == Guid.Empty ||
            message.Items.Count > MaximumPageSize ||
            message.IsLastPage != (message.NextAfterProductId is null) ||
            (!message.IsLastPage && message.Items.Count == 0) ||
            (!message.IsLastPage && message.Items[^1].ProductId != message.NextAfterProductId) ||
            message.Items.Any(item =>
                item.ProductId == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.Sku) ||
                item.Sku.Length > 64 ||
                string.IsNullOrWhiteSpace(item.Name) ||
                item.Name.Length > 200 ||
                item.Price < 0m ||
                string.IsNullOrWhiteSpace(item.CurrencyCode) ||
                item.CurrencyCode.Length != 3 ||
                item.Version <= 0) ||
            message.Items.Select(item => item.ProductId).Distinct().Count() != message.Items.Count)
        {
            throw new OrderWorkflowException("order.invalid_product_snapshot_page");
        }
    }
}
