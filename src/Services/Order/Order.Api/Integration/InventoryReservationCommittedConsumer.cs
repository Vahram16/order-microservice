using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class InventoryReservationCommittedConsumer(OrderDbContext dbContext, IIntegrationCommandSender<CaptureOrderPayment> captureSender, IIntegrationCommandSender<ReleaseInventory> releaseSender, TimeProvider timeProvider)
    : IConsumer<InventoryReservationCommitted>
{
    public async Task Consume(ConsumeContext<InventoryReservationCommitted> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(item => item.Id == context.Message.OrderId, context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");
        if (order.Status == OrderStatus.Confirmed) return;
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired)
        {
            await releaseSender.SendAsync(new ReleaseInventory(order.Id, context.Message.ReservationId, "late_inventory_commit"), new IntegrationMessageMetadata(CorrelationId: order.Id), context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var transition = order.MarkInventoryCommitted(context.Message.ReservationId, timeProvider.GetUtcNow());
        if (transition.IsFailure) throw new OrderWorkflowException(transition.Error.Code);
        if (order.PaymentAttemptId is not { } paymentAttemptId) throw new OrderWorkflowException("order.payment_attempt_missing");
        await captureSender.SendAsync(new CaptureOrderPayment(order.Id, paymentAttemptId), new IntegrationMessageMetadata(MessageId: paymentAttemptId, CorrelationId: order.Id), context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
