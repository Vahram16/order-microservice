using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class InventoryRejectedConsumer(
    OrderDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<InventoryRejected>
{
    public async Task Consume(ConsumeContext<InventoryRejected> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == context.Message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");
        if (order.IsTerminal)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var transition = order.Cancel("inventory_unavailable", now);
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        await eventPublisher.PublishAsync(
            new OrderCancelled(order.Id, order.CustomerId, "inventory_unavailable", now),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
