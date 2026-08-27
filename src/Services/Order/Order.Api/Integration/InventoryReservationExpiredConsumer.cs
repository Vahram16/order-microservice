using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
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
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == context.Message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");
        if (order.IsTerminal)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var transition = order.Expire(now < order.ExpiresAt ? order.ExpiresAt : now);
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        if (order.PaymentAttemptId is { } paymentAttemptId)
        {
            await cancelPaymentSender.SendAsync(
                new CancelOrderPayment(order.Id, paymentAttemptId, "inventory_expired"),
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
