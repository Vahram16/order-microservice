using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class CaptureOrderPaymentConsumer(PaymentDbContext dbContext, IOrderPaymentProvider provider, IIntegrationEventPublisher eventPublisher, TimeProvider timeProvider)
    : IConsumer<CaptureOrderPayment>
{
    public async Task Consume(ConsumeContext<CaptureOrderPayment> context)
    {
        var message = context.Message;
        var attempt = await dbContext.OrderPaymentAttempts.SingleOrDefaultAsync(item => item.OrderId == message.OrderId && item.Id == message.PaymentAttemptId, context.CancellationToken)
            ?? throw PaymentWorkflowException.Transient("payment.order.attempt_not_registered");

        if (attempt.Status == OrderPaymentStatus.Captured)
        {
            await PublishCapturedAsync(attempt, context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }
        if (attempt.Status == OrderPaymentStatus.CaptureFailed)
        {
            await PublishFailedAsync(attempt, context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }
        if (attempt.Status != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
            throw PaymentWorkflowException.Permanent("payment.order.capture_invalid_state");

        var customer = await dbContext.PaymentCustomers.AsNoTracking().SingleAsync(item => item.Id == attempt.PaymentCustomerId, context.CancellationToken);
        var method = await dbContext.PaymentMethods.AsNoTracking().SingleAsync(item => item.Id == attempt.PaymentMethodId, context.CancellationToken);
        if (string.IsNullOrWhiteSpace(customer.ProviderCustomerId)) throw PaymentWorkflowException.Permanent("payment.order.provider_customer_not_ready");

        try
        {
            var session = await provider.GetAsync(attempt.ProviderPaymentIntentId, context.CancellationToken);
            if (string.Equals(session.Status, "requires_capture", StringComparison.Ordinal))
                session = await provider.CaptureAsync(session.ProviderPaymentIntentId, OrderPaymentProviderIdempotencyKeys.Capture(attempt.OrderId), context.CancellationToken);

            if (!OrderPaymentProviderSessionValidator.Matches(session, attempt.ProviderPaymentIntentId, customer.ProviderCustomerId, method.ProviderPaymentMethodId, attempt.Amount, attempt.CurrencyCode))
                throw PaymentWorkflowException.Permanent("payment.order.provider_state_mismatch");

            await ApplyCaptureStateAsync(attempt, session.Status, context.CancellationToken);
        }
        catch (PaymentProviderException exception) when (exception.FailureKind == PaymentProviderFailureKind.Transient)
        {
            throw PaymentWorkflowException.Transient(exception.Code, exception);
        }
        catch (PaymentProviderException exception)
        {
            await FailAsync(attempt, exception.Code, context.CancellationToken);
        }
    }

    private async Task ApplyCaptureStateAsync(OrderPaymentAttempt attempt, string status, CancellationToken cancellationToken)
    {
        switch (status)
        {
            case "succeeded":
                Ensure(attempt.Capture(timeProvider.GetUtcNow()));
                await PublishCapturedAsync(attempt, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            case "processing":
            case "requires_capture":
                throw PaymentWorkflowException.Transient("payment.order.capture_pending");
            case "requires_payment_method":
            case "canceled":
            case "requires_action":
                await FailAsync(attempt, $"payment.order.capture_{status}", cancellationToken);
                return;
            default:
                throw PaymentWorkflowException.Transient("payment.order.capture_pending");
        }
    }

    private async Task FailAsync(OrderPaymentAttempt attempt, string code, CancellationToken cancellationToken)
    {
        Ensure(attempt.FailCapture(code, timeProvider.GetUtcNow()));
        await PublishFailedAsync(attempt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task PublishCapturedAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken) =>
        eventPublisher.PublishAsync(new PaymentCaptured(attempt.OrderId, attempt.Id, attempt.Amount, attempt.CurrencyCode, attempt.UpdatedAt), cancellationToken: cancellationToken);

    private Task PublishFailedAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken) =>
        eventPublisher.PublishAsync(new PaymentCaptureFailed(attempt.OrderId, attempt.Id, attempt.RejectionCode ?? "capture_failed", attempt.UpdatedAt), cancellationToken: cancellationToken);

    private static void Ensure(Result result)
    {
        if (result.IsFailure) throw PaymentWorkflowException.Permanent(result.Error.Code);
    }
}
