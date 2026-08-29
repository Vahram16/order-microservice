using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class PaymentCancelledConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<ReleaseInventory> releaseInventorySender,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<PaymentCancelled>
{
    public async Task Consume(ConsumeContext<PaymentCancelled> context)
    {
        var message = context.Message;
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");

        if (order.PaymentAttemptId is { } existingPaymentAttemptId &&
            existingPaymentAttemptId != message.PaymentAttemptId)
        {
            throw new OrderWorkflowException("order.payment_attempt_mismatch");
        }

        // Cancellation/expiry initiated by Order commonly causes this fact. Redelivery and late
        // acknowledgement are therefore expected and must be idempotent.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired)
        {
            return;
        }

        if (order.PaymentAttemptId is null)
        {
            throw new OrderWorkflowException("order.payment_attempt_missing");
        }

        // A confirmed Order cannot be silently reopened by a late cancellation fact. This is a
        // financial invariant breach that must surface for operator/reconciliation handling.
        if (order.Status == OrderStatus.Confirmed)
        {
            throw new OrderWorkflowException("order.payment_cancelled_after_confirmation");
        }

        var now = timeProvider.GetUtcNow();
        var transition = order.Cancel("payment_cancelled", now);
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        if (order.InventoryReservationId is { } reservationId)
        {
            await releaseInventorySender.SendAsync(
                new ReleaseInventory(order.Id, reservationId, "payment_cancelled"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
        }

        await eventPublisher.PublishAsync(
            new OrderCancelled(order.Id, order.CustomerId, "payment_cancelled", now),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
