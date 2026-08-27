using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class CancelOrderConsumer(PaymentDbContext dbContext, IOrderPaymentProvider provider, IIntegrationEventPublisher eventPublisher, TimeProvider timeProvider)
    : IConsumer<CancelOrderPayment>
{
    public async Task Consume(ConsumeContext<CancelOrderPayment> context)
    {
        var attempt = await dbContext.OrderPaymentAttempts.SingleOrDefaultAsync(item => item.OrderId == context.Message.OrderId && item.Id == context.Message.PaymentAttemptId, context.CancellationToken);
        if (attempt is null) throw PaymentWorkflowException.Transient("payment.order.attempt_not_registered");
        if (attempt.Status == OrderPaymentStatus.Captured) throw PaymentWorkflowException.Permanent("payment.order.captured_payment_requires_refund");
        if (attempt.Status == OrderPaymentStatus.Rejected) return;
        if (attempt.Status == OrderPaymentStatus.Cancelled)
        {
            await eventPublisher.PublishAsync(new PaymentCancelled(attempt.OrderId, attempt.Id, attempt.UpdatedAt), cancellationToken: context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
            {
                var session = await provider.CancelAsync(attempt.ProviderPaymentIntentId, OrderPaymentProviderIdempotencyKeys.Cancel(attempt.OrderId), context.CancellationToken);
                if (!string.Equals(session.Status, "canceled", StringComparison.Ordinal)) throw PaymentWorkflowException.Permanent("payment.order.cancel_not_confirmed");
            }
            var now = timeProvider.GetUtcNow();
            var cancelled = attempt.Cancel(now);
            if (cancelled.IsFailure) throw PaymentWorkflowException.Permanent(cancelled.Error.Code);
            await eventPublisher.PublishAsync(new PaymentCancelled(attempt.OrderId, attempt.Id, now), cancellationToken: context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            throw exception.FailureKind == PaymentProviderFailureKind.Transient
                ? PaymentWorkflowException.Transient(exception.Code, exception)
                : PaymentWorkflowException.Permanent(exception.Code, exception);
        }
    }
}
