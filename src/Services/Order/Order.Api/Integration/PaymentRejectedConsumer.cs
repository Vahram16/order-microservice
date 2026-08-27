using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class PaymentRejectedConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<ReleaseInventory> releaseSender,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<PaymentRejected>
{
    public async Task Consume(ConsumeContext<PaymentRejected> context)
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
        var transition = order.Cancel("payment_rejected", now);
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        if (order.InventoryReservationId is { } reservationId)
        {
            await releaseSender.SendAsync(
                new ReleaseInventory(order.Id, reservationId, "payment_rejected"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
        }

        await eventPublisher.PublishAsync(
            new OrderCancelled(order.Id, order.CustomerId, "payment_rejected", now),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
