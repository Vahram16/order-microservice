using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class CancelOrderConsumer(
    PaymentDbContext dbContext,
    IOrderPaymentProvider provider,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<CancelOrderPayment>
{
    public async Task Consume(ConsumeContext<CancelOrderPayment> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var attempt = await dbContext.OrderPaymentAttempts.SingleOrDefaultAsync(
            item => item.OrderId == message.OrderId && item.Id == message.PaymentAttemptId,
            cancellationToken);
        if (attempt is null)
        {
            throw PaymentWorkflowException.Transient("payment.order.attempt_not_registered");
        }

        if (attempt.Status == OrderPaymentStatus.Rejected)
        {
            return;
        }

        if (attempt.Status == OrderPaymentStatus.Cancelled)
        {
            await eventPublisher.PublishAsync(
                new PaymentCancelled(attempt.OrderId, attempt.Id, attempt.UpdatedAt),
                cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
            {
                var session = await provider.CancelAsync(
                    attempt.ProviderPaymentIntentId,
                    OrderPaymentProviderIdempotencyKeys.Cancel(attempt.OrderId),
                    cancellationToken);
                if (!string.Equals(session.Status, "canceled", StringComparison.Ordinal))
                {
                    throw PaymentWorkflowException.Permanent("payment.order.cancel_not_confirmed");
                }
            }

            var now = timeProvider.GetUtcNow();
            var cancelled = attempt.Cancel(now);
            if (cancelled.IsFailure)
            {
                throw PaymentWorkflowException.Permanent(cancelled.Error.Code);
            }

            await eventPublisher.PublishAsync(
                new PaymentCancelled(attempt.OrderId, attempt.Id, now),
                cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            throw exception.FailureKind == PaymentProviderFailureKind.Transient
                ? PaymentWorkflowException.Transient(exception.Code, exception)
                : PaymentWorkflowException.Permanent(exception.Code, exception);
        }
    }
}
