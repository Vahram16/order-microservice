using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Product.Api.Persistence;

namespace Product.Api.Integration;

internal sealed class ProductCatalogSnapshotConsumer(
    ProductDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    IIntegrationCommandSender<SynchronizeProductCatalogSnapshot> commandSender,
    TimeProvider timeProvider) : IConsumer<SynchronizeProductCatalogSnapshot>
{
    private const int MaximumPageSize = 500;

    public async Task Consume(ConsumeContext<SynchronizeProductCatalogSnapshot> context)
    {
        var message = context.Message;
        if (message.SnapshotId == Guid.Empty || message.PageSize is <= 0 or > MaximumPageSize)
            throw new ProductSnapshotException("product.snapshot.invalid_request");

        var query = dbContext.Products.AsNoTracking().OrderBy(product => product.Id);
        if (message.AfterProductId is { } afterProductId)
            query = (IOrderedQueryable<Domain.Product>)query.Where(product => product.Id.CompareTo(afterProductId) > 0).OrderBy(product => product.Id);

        var page = await query.Take(message.PageSize + 1).ToListAsync(context.CancellationToken);
        var current = page.Take(message.PageSize).ToArray();
        foreach (var product in current)
        {
            await eventPublisher.PublishAsync(
                new ProductCatalogChanged(product.Id, product.Sku, product.Name, product.Price, product.CurrencyCode, product.Version, IsAvailable: true, product.UpdatedAt),
                cancellationToken: context.CancellationToken);
        }

        if (page.Count > message.PageSize)
        {
            await commandSender.SendAsync(
                new SynchronizeProductCatalogSnapshot(message.SnapshotId, current[^1].Id, message.PageSize),
                new IntegrationMessageMetadata(CorrelationId: message.SnapshotId),
                context.CancellationToken);
            return;
        }

        await eventPublisher.PublishAsync(
            new ProductCatalogSnapshotCompleted(message.SnapshotId, timeProvider.GetUtcNow()),
            new IntegrationMessageMetadata(CorrelationId: message.SnapshotId),
            context.CancellationToken);
    }

    private sealed class ProductSnapshotException(string code) : Exception(code), Microservices.Messaging.IPermanentConsumerFailure;
}
