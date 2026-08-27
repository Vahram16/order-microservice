using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class CancelOrderConsumer(PaymentDbContext dbContext, IOrderPaymentProvider provider, IIntegrationEventPublisher eventPublisher, TimeProvider timeProvider)
    : IConsumer<CancelOrderPayment>
{
    public async Task Consume(ConsumeContext<CancelOrderPayment> context)
    {
        var attempt = await dbContext.OrderPaymentAttempts.SingleOrDefaultAsync(item => item.OrderId == context.Message.OrderId && item.Id == context.Message.PaymentAttemptId, context.CancellationToken);
        if (attempt is null) throw PaymentWorkflowException.Transient("payment.order.attempt_not_registered");
        if (attempt.Status is OrderPaymentStatus.Rejected or OrderPaymentStatus.Cancelled) return;
        if (attempt.Status == OrderPaymentStatus.Refunded) return;

        try
        {
            var now = timeProvider.GetUtcNow();
            var cancellationRequested = attempt.RequestCancellation(now);
            if (cancellationRequested.IsFailure) throw PaymentWorkflowException.Permanent(cancellationRequested.Error.Code);
            await dbContext.SaveChangesAsync(context.CancellationToken);

            if (string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
            {
                await MarkCancelledAsync(attempt, context.CancellationToken);
                return;
            }

            await ReconcileProviderCancellationAsync(attempt, context.CancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            throw exception.FailureKind == PaymentProviderFailureKind.Transient
                ? PaymentWorkflowException.Transient(exception.Code, exception)
                : PaymentWorkflowException.Permanent(exception.Code, exception);
        }
    }

    private async Task ReconcileProviderCancellationAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var session = await provider.GetAsync(attempt.ProviderPaymentIntentId!, cancellationToken);
        if (string.Equals(session.Status, "succeeded", StringComparison.Ordinal))
        {
            await RefundCapturedAsync(attempt, cancellationToken);
            return;
        }
        if (string.Equals(session.Status, "canceled", StringComparison.Ordinal))
        {
            await MarkCancelledAsync(attempt, cancellationToken);
            return;
        }

        try
        {
            session = await provider.CancelAsync(
                attempt.ProviderPaymentIntentId!,
                OrderPaymentProviderIdempotencyKeys.Cancel(attempt.OrderId),
                cancellationToken);
        }
        catch (PaymentProviderException exception) when (exception.FailureKind == PaymentProviderFailureKind.Permanent)
        {
            session = await provider.GetAsync(attempt.ProviderPaymentIntentId!, cancellationToken);
            if (!string.Equals(session.Status, "succeeded", StringComparison.Ordinal))
            {
                throw;
            }
        }

        if (string.Equals(session.Status, "succeeded", StringComparison.Ordinal))
        {
            await RefundCapturedAsync(attempt, cancellationToken);
            return;
        }
        if (!string.Equals(session.Status, "canceled", StringComparison.Ordinal))
        {
            throw PaymentWorkflowException.Transient("payment.order.cancel_pending");
        }

        await MarkCancelledAsync(attempt, cancellationToken);
    }

    private async Task RefundCapturedAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.Status == OrderPaymentStatus.CancellationRequested)
        {
            var captured = attempt.ObserveCapturedDuringCancellation(timeProvider.GetUtcNow());
            if (captured.IsFailure) throw PaymentWorkflowException.Permanent(captured.Error.Code);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var refund = string.IsNullOrWhiteSpace(attempt.ProviderRefundId)
            ? await provider.RefundAsync(
                attempt.ProviderPaymentIntentId!,
                OrderPaymentProviderIdempotencyKeys.Refund(attempt.OrderId),
                cancellationToken)
            : await provider.GetRefundAsync(attempt.ProviderRefundId, cancellationToken);

        ValidateRefund(attempt, refund);
        var now = timeProvider.GetUtcNow();
        switch (refund.Status)
        {
            case "succeeded":
                Ensure(attempt.MarkRefunded(refund.ProviderRefundId, now));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            case "pending":
            case "requires_action":
                Ensure(attempt.MarkRefundPending(refund.ProviderRefundId, now));
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            case "failed":
            case "canceled":
                Ensure(attempt.FailRefund(refund.ProviderRefundId, refund.FailureReason ?? "refund_failed", now));
                await dbContext.SaveChangesAsync(cancellationToken);
                throw PaymentWorkflowException.Permanent("payment.order.refund_failed");
            default:
                throw PaymentWorkflowException.Transient("payment.order.refund_pending");
        }
    }

    private async Task MarkCancelledAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        Ensure(attempt.Cancel(now));
        await eventPublisher.PublishAsync(new PaymentCancelled(attempt.OrderId, attempt.Id, now), cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateRefund(OrderPaymentAttempt attempt, OrderPaymentRefundSession refund)
    {
        if (string.IsNullOrWhiteSpace(refund.ProviderRefundId) ||
            !string.Equals(refund.ProviderPaymentIntentId, attempt.ProviderPaymentIntentId, StringComparison.Ordinal) ||
            refund.Amount != attempt.Amount ||
            !string.Equals(refund.CurrencyCode, attempt.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw PaymentWorkflowException.Permanent("payment.order.refund_state_mismatch");
        }
    }

    private static void Ensure(Result result)
    {
        if (result.IsFailure) throw PaymentWorkflowException.Permanent(result.Error.Code);
    }
}