using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class PaymentCapturedConsumer(OrderDbContext dbContext, IIntegrationEventPublisher eventPublisher, TimeProvider timeProvider) : IConsumer<PaymentCaptured>
{
    public async Task Consume(ConsumeContext<PaymentCaptured> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(item => item.Id == context.Message.OrderId, context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");
        if (order.Status == OrderStatus.Confirmed) return;
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired) throw new OrderWorkflowException("order.late_captured_payment_requires_reconciliation");
        var now = timeProvider.GetUtcNow();
        var transition = order.ConfirmPaymentCaptured(context.Message.PaymentAttemptId, context.Message.Amount, context.Message.CurrencyCode, now);
        if (transition.IsFailure) throw new OrderWorkflowException(transition.Error.Code);
        await eventPublisher.PublishAsync(new OrderConfirmed(order.Id, order.CustomerId, order.Total, order.CurrencyCode, now), new IntegrationMessageMetadata(CorrelationId: order.Id), context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
