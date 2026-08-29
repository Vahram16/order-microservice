using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class PaymentCaptureFailedConsumer(OrderDbContext dbContext, IIntegrationCommandSender<ReleaseInventory> releaseSender, IIntegrationCommandSender<CancelOrderPayment> cancelPaymentSender, IIntegrationEventPublisher eventPublisher, TimeProvider timeProvider)
    : IConsumer<PaymentCaptureFailed>
{
    public async Task Consume(ConsumeContext<PaymentCaptureFailed> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(item => item.Id == context.Message.OrderId, context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");
        if (order.IsTerminal) return;
        var now = timeProvider.GetUtcNow();
        var transition = order.FailPaymentCapture(context.Message.PaymentAttemptId, "payment_capture_failed", now);
        if (transition.IsFailure) throw new OrderWorkflowException(transition.Error.Code);
        if (order.InventoryReservationId is not { } reservationId) throw new OrderWorkflowException("order.inventory_reservation_missing");
        await releaseSender.SendAsync(new ReleaseInventory(order.Id, reservationId, "payment_capture_failed"), new IntegrationMessageMetadata(CorrelationId: order.Id), context.CancellationToken);
        await cancelPaymentSender.SendAsync(new CancelOrderPayment(order.Id, context.Message.PaymentAttemptId, "payment_capture_failed"), new IntegrationMessageMetadata(CorrelationId: order.Id), context.CancellationToken);
        await eventPublisher.PublishAsync(new OrderCancelled(order.Id, order.CustomerId, "payment_capture_failed", now), new IntegrationMessageMetadata(CorrelationId: order.Id), context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
