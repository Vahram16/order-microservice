using MassTransit;
using Microservices.Contracts.Products.V1;
using Microservices.Primitives;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class ProductCatalogChangedConsumer(OrderDbContext dbContext) : IConsumer<ProductCatalogChanged>
{
    public async Task Consume(ConsumeContext<ProductCatalogChanged> context)
    {
        var message = context.Message;
        if (message.ProductId == Guid.Empty || message.Version <= 0 ||
            string.IsNullOrWhiteSpace(message.Sku) || string.IsNullOrWhiteSpace(message.Name) ||
            message.Price < 0m || !CurrencyAmount.TryNormalizeCurrencyCode(message.CurrencyCode, out var currencyCode) ||
            !CurrencyAmount.HasValidScale(message.Price, currencyCode))
        {
            throw new OrderWorkflowException("order.invalid_catalog_event");
        }

        var product = await dbContext.OrderProducts.SingleOrDefaultAsync(item => item.ProductId == message.ProductId, context.CancellationToken);
        if (product is null)
        {
            dbContext.OrderProducts.Add(OrderProductProjection.Create(message.ProductId, message.Sku, message.Name, message.Price, currencyCode, message.Version, message.IsAvailable, message.OccurredAtUtc));
        }
        else
        {
            product.Apply(message.Sku, message.Name, message.Price, currencyCode, message.Version, message.IsAvailable, message.OccurredAtUtc);
        }
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
