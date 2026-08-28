using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class InventoryReservationExpiredConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<CancelOrderPayment> cancelPaymentSender,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<InventoryReservationExpired>
{
    public async Task Consume(ConsumeContext<InventoryReservationExpired> context)
    {
        var message = context.Message;
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");

        if (order.InventoryReservationId is { } reservationId && reservationId != message.ReservationId)
        {
            throw new OrderWorkflowException("order.inventory_reservation_mismatch");
        }

        if (order.IsTerminal)
        {
            return;
        }

        // PaymentCapturing means Inventory already acknowledged CommitInventoryReservation. An expiry
        // fact delivered afterwards is stale and must not unwind a committed reservation or a charge
        // that may already have completed at the provider.
        if (order.Status == OrderStatus.PaymentCapturing)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            var paymentAttemptId = order.PaymentAttemptId
                ?? throw new OrderWorkflowException("order.payment_attempt_missing");
            var cancellation = order.Cancel("inventory_expired", now);
            if (cancellation.IsFailure)
            {
                throw new OrderWorkflowException(cancellation.Error.Code);
            }

            await cancelPaymentSender.SendAsync(
                new CancelOrderPayment(order.Id, paymentAttemptId, "inventory_expired"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
            await eventPublisher.PublishAsync(
                new OrderCancelled(order.Id, order.CustomerId, "inventory_expired", now),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var transition = order.Expire(now < order.ExpiresAt ? order.ExpiresAt : now);
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        if (order.PaymentAttemptId is { } existingPaymentAttemptId)
        {
            await cancelPaymentSender.SendAsync(
                new CancelOrderPayment(order.Id, existingPaymentAttemptId, "inventory_expired"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
        }

        await eventPublisher.PublishAsync(
            new OrderExpired(order.Id, order.CustomerId, now),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
