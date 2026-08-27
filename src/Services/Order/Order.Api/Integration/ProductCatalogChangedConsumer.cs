using MassTransit;
using Microservices.Contracts.Products.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class ProductCatalogChangedConsumer(OrderDbContext dbContext)
    : IConsumer<ProductCatalogChanged>
{
    public async Task Consume(ConsumeContext<ProductCatalogChanged> context)
    {
        var message = context.Message;
        if (message.ProductId == Guid.Empty || message.Version <= 0 ||
            string.IsNullOrWhiteSpace(message.Sku) || string.IsNullOrWhiteSpace(message.Name) ||
            message.Price < 0m || decimal.Round(message.Price, 2) != message.Price ||
            string.IsNullOrWhiteSpace(message.CurrencyCode) || message.CurrencyCode.Length != 3)
        {
            throw new OrderWorkflowException("order.invalid_catalog_event");
        }

        var product = await dbContext.OrderProducts.SingleOrDefaultAsync(
            item => item.ProductId == message.ProductId,
            context.CancellationToken);
        if (product is null)
        {
            dbContext.OrderProducts.Add(OrderProductProjection.Create(
                message.ProductId,
                message.Sku,
                message.Name,
                message.Price,
                message.CurrencyCode.ToUpperInvariant(),
                message.Version,
                message.IsAvailable,
                message.OccurredAtUtc));
        }
        else
        {
            product.Apply(
                message.Sku,
                message.Name,
                message.Price,
                message.CurrencyCode.ToUpperInvariant(),
                message.Version,
                message.IsAvailable,
                message.OccurredAtUtc);
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
