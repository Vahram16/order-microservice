using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class PaymentAuthorizedConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<CommitInventoryReservation> inventorySender,
    IIntegrationCommandSender<CancelOrderPayment> cancelPaymentSender,
    TimeProvider timeProvider) : IConsumer<PaymentAuthorized>
{
    public async Task Consume(ConsumeContext<PaymentAuthorized> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == context.Message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired)
        {
            await cancelPaymentSender.SendAsync(
                new CancelOrderPayment(order.Id, context.Message.PaymentAttemptId, "late_authorization"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
            return;
        }

        var transition = order.MarkPaymentAuthorized(
            context.Message.PaymentAttemptId,
            context.Message.Amount,
            context.Message.CurrencyCode,
            timeProvider.GetUtcNow());
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        if (order.InventoryReservationId is not { } reservationId)
        {
            throw new OrderWorkflowException("order.inventory_reservation_missing");
        }

        await inventorySender.SendAsync(
            new CommitInventoryReservation(order.Id, reservationId),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
