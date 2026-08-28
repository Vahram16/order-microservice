using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class OrderPaymentCompensationService(
    PaymentDbContext dbContext,
    IOrderPaymentProvider provider,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider)
{
    public async Task ReconcileAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.Status == OrderPaymentStatus.Refunded) return;

        if (!string.IsNullOrWhiteSpace(attempt.ProviderRefundId))
        {
            var existingRefund = await provider.GetRefundAsync(attempt.ProviderRefundId, cancellationToken);
            await ApplyRefundAsync(attempt, existingRefund, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
        {
            await MarkCancelledAsync(attempt, cancellationToken);
            return;
        }

        var session = await provider.GetAsync(attempt.ProviderPaymentIntentId, cancellationToken);
        if (string.Equals(session.Status, "succeeded", StringComparison.Ordinal))
        {
            await CreateRefundAsync(attempt, cancellationToken);
            return;
        }
        if (string.Equals(session.Status, "canceled", StringComparison.Ordinal))
        {
            await MarkCancelledAsync(attempt, cancellationToken);
            return;
        }

        try
        {
            session = await provider.CancelAsync(attempt.ProviderPaymentIntentId, OrderPaymentProviderIdempotencyKeys.Cancel(attempt.OrderId), cancellationToken);
        }
        catch (PaymentProviderException exception) when (exception.FailureKind == PaymentProviderFailureKind.Permanent)
        {
            session = await provider.GetAsync(attempt.ProviderPaymentIntentId, cancellationToken);
            if (!string.Equals(session.Status, "succeeded", StringComparison.Ordinal)) throw;
        }

        if (string.Equals(session.Status, "succeeded", StringComparison.Ordinal))
        {
            await CreateRefundAsync(attempt, cancellationToken);
            return;
        }
        if (!string.Equals(session.Status, "canceled", StringComparison.Ordinal))
            throw PaymentWorkflowException.Transient("payment.order.cancel_pending");
        await MarkCancelledAsync(attempt, cancellationToken);
    }

    public async Task RecordFailureAsync(OrderPaymentAttempt attempt, string failureCode, CancellationToken cancellationToken)
    {
        var recorded = attempt.RecordCompensationFailure(failureCode, timeProvider.GetUtcNow());
        Ensure(recorded);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateRefundAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var refund = await provider.RefundAsync(attempt.ProviderPaymentIntentId!, OrderPaymentProviderIdempotencyKeys.Refund(attempt.OrderId), cancellationToken);
        await ApplyRefundAsync(attempt, refund, cancellationToken);
    }

    private async Task ApplyRefundAsync(OrderPaymentAttempt attempt, OrderPaymentRefundSession refund, CancellationToken cancellationToken)
    {
        ValidateRefund(attempt, refund);
        var now = timeProvider.GetUtcNow();
        switch (refund.Status)
        {
            case "succeeded": Ensure(attempt.MarkRefunded(refund.ProviderRefundId, now)); break;
            case "pending":
            case "requires_action": Ensure(attempt.MarkRefundPending(refund.ProviderRefundId, now)); break;
            case "failed":
            case "canceled": Ensure(attempt.FailRefund(refund.ProviderRefundId, refund.FailureReason ?? "refund_failed", now)); break;
            default: throw PaymentWorkflowException.Transient("payment.order.refund_pending");
        }
        await dbContext.SaveChangesAsync(cancellationToken);
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
            throw PaymentWorkflowException.Permanent("payment.order.refund_state_mismatch");
    }

    private static void Ensure(Result result)
    {
        if (result.IsFailure) throw PaymentWorkflowException.Permanent(result.Error.Code);
    }
}
