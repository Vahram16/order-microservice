using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class InventoryReservationCommittedConsumer(
    OrderDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<InventoryReservationCommitted>
{
    public async Task Consume(ConsumeContext<InventoryReservationCommitted> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == context.Message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");
        if (order.Status == Domain.OrderStatus.Confirmed)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var transition = order.Confirm(context.Message.ReservationId, now);
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        await eventPublisher.PublishAsync(
            new OrderConfirmed(order.Id, order.CustomerId, order.Total, order.CurrencyCode, now),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
