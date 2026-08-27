using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Products.V1;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Product.Api.Persistence;

namespace Product.Api.Integration;

internal sealed class ProductCatalogSnapshotConsumer(
    ProductDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider)
    : IConsumer<SynchronizeProductCatalogSnapshot>
{
    private const int MaximumPageSize = 500;

    public async Task Consume(ConsumeContext<SynchronizeProductCatalogSnapshot> context)
    {
        var message = context.Message;
        if (message.SnapshotId == Guid.Empty || message.PageSize is <= 0 or > MaximumPageSize)
        {
            throw new ProductSnapshotException("product.snapshot.invalid_request");
        }

        IQueryable<Domain.Product> query = dbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.Id);

        if (message.AfterProductId is { } afterProductId)
        {
            query = query
                .Where(product => product.Id.CompareTo(afterProductId) > 0)
                .OrderBy(product => product.Id);
        }

        var page = await query
            .Take(message.PageSize + 1)
            .ToListAsync(context.CancellationToken);
        var items = page
            .Take(message.PageSize)
            .Select(product => new ProductCatalogSnapshotItem(
                product.Id,
                product.Sku,
                product.Name,
                product.Price,
                product.CurrencyCode,
                product.Version,
                product.UpdatedAt))
            .ToArray();
        var hasMore = page.Count > message.PageSize;
        var nextAfterProductId = hasMore ? items[^1].ProductId : (Guid?)null;

        await eventPublisher.PublishAsync(
            new ProductCatalogSnapshotPage(
                message.SnapshotId,
                message.AfterProductId,
                items,
                nextAfterProductId,
                IsLastPage: !hasMore,
                OccurredAtUtc: timeProvider.GetUtcNow()),
            new IntegrationMessageMetadata(CorrelationId: message.SnapshotId),
            context.CancellationToken);
    }

    private sealed class ProductSnapshotException(string code)
        : Exception(code), IPermanentConsumerFailure;
}

internal sealed class ProductCatalogSnapshotConsumerDefinition
    : ConsumerDefinition<ProductCatalogSnapshotConsumer>
{
    public ProductCatalogSnapshotConsumerDefinition()
    {
        EndpointName = SynchronizeProductCatalogSnapshot.EndpointName;
    }
}
