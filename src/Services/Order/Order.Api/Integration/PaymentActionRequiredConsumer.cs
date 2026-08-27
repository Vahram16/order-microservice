using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class PaymentActionRequiredConsumer(
    OrderDbContext dbContext,
    IIntegrationCommandSender<CancelOrderPayment> cancelPaymentSender,
    TimeProvider timeProvider) : IConsumer<PaymentActionRequired>
{
    public async Task Consume(ConsumeContext<PaymentActionRequired> context)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(
            item => item.Id == context.Message.OrderId,
            context.CancellationToken)
            ?? throw new OrderWorkflowException("order.workflow_order_not_found");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired)
        {
            await cancelPaymentSender.SendAsync(
                new CancelOrderPayment(order.Id, context.Message.PaymentAttemptId, "order_terminal"),
                new IntegrationMessageMetadata(CorrelationId: order.Id),
                context.CancellationToken);
            return;
        }

        var transition = order.RequirePaymentAction(
            context.Message.PaymentAttemptId,
            context.Message.ExpiresAtUtc,
            timeProvider.GetUtcNow());
        if (transition.IsFailure)
        {
            throw new OrderWorkflowException(transition.Error.Code);
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
