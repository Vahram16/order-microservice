using MassTransit;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class CancelOrderConsumer(PaymentDbContext dbContext, OrderPaymentCompensationService compensation, TimeProvider timeProvider) : IConsumer<CancelOrderPayment>
{
    public async Task Consume(ConsumeContext<CancelOrderPayment> context)
    {
        var attempt = await dbContext.OrderPaymentAttempts.SingleOrDefaultAsync(item => item.OrderId == context.Message.OrderId && item.Id == context.Message.PaymentAttemptId, context.CancellationToken);
        if (attempt is null) throw PaymentWorkflowException.Transient("payment.order.attempt_not_registered");
        if (attempt.Status is OrderPaymentStatus.Rejected or OrderPaymentStatus.Cancelled or OrderPaymentStatus.Refunded) return;

        var cancellationRequested = attempt.RequestCancellation(timeProvider.GetUtcNow());
        if (cancellationRequested.IsFailure) throw PaymentWorkflowException.Permanent(cancellationRequested.Error.Code);
        await dbContext.SaveChangesAsync(context.CancellationToken);

        try { await compensation.ReconcileAsync(attempt, context.CancellationToken); }
        catch (PaymentProviderException exception)
        {
            await compensation.RecordFailureAsync(attempt, exception.Code, context.CancellationToken);
            if (exception.FailureKind == PaymentProviderFailureKind.Transient)
                throw PaymentWorkflowException.Transient(exception.Code, exception);
        }
    }
}
