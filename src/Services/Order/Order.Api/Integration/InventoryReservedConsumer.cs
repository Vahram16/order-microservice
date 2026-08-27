using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class InventoryReservedConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<AuthorizeOrderPayment> paymentSender,
    IIntegrationCommandSender<ReleaseInventory> releaseSender,
    TimeProvider timeProvider) : IConsumer<InventoryReserved>
{
    public async Task Consume(ConsumeContext<InventoryReserved> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == context.Message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired)
        {
            await releaseSender.SendAsync(
                new ReleaseInventory(order.Id, context.Message.ReservationId, "order_terminal"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
            return;
        }

        if (order.Status == OrderStatus.Confirmed)
        {
            return;
        }

        var transition = order.MarkInventoryReserved(
            context.Message.ReservationId,
            context.Message.ExpiresAtUtc,
            timeProvider.GetUtcNow());
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        await paymentSender.SendAsync(
            new AuthorizeOrderPayment(
                order.Id,
                order.CustomerId,
                order.PaymentMethodId,
                order.Total,
                order.CurrencyCode,
                order.ExpiresAt),
            new IntegrationMessageMetadata(CorrelationId: order.Id),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
