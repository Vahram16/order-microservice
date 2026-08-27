using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class AuthorizeOrderConsumer(
    PaymentDbContext dbContext,
    IOrderPaymentProvider provider,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<AuthorizeOrderPayment>
{
    public async Task Consume(ConsumeContext<AuthorizeOrderPayment> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var customer = await dbContext.PaymentCustomers.SingleOrDefaultAsync(
            item => item.CustomerId == message.CustomerId,
            cancellationToken);
        if (customer is null)
        {
            throw PaymentWorkflowException.Transient("payment.order.customer_not_synchronized");
        }

        var attempt = await dbContext.OrderPaymentAttempts.SingleOrDefaultAsync(
            item => item.OrderId == message.OrderId,
            cancellationToken);
        if (attempt is null)
        {
            var created = OrderPaymentAttempt.Create(
                Guid.NewGuid(),
                message.OrderId,
                customer.Id,
                message.PaymentMethodId,
                message.Amount,
                message.CurrencyCode,
                message.ExpiresAtUtc,
                timeProvider.GetUtcNow());
            if (created.IsFailure)
            {
                throw PaymentWorkflowException.Permanent(created.Error.Code);
            }

            attempt = created.Value;
            dbContext.OrderPaymentAttempts.Add(attempt);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (
                exception.IsUniqueConstraintViolation(PaymentDatabaseConstraints.OrderPaymentOrder))
            {
                dbContext.ChangeTracker.Clear();
                customer = await dbContext.PaymentCustomers.SingleAsync(
                    item => item.CustomerId == message.CustomerId,
                    cancellationToken);
                attempt = await dbContext.OrderPaymentAttempts.SingleAsync(
                    item => item.OrderId == message.OrderId,
                    cancellationToken);
            }
        }

        if (!attempt.MatchesRequest(
                message.CustomerId,
                message.PaymentMethodId,
                message.Amount,
                message.CurrencyCode,
                message.ExpiresAtUtc,
                customer.CustomerId))
        {
            throw PaymentWorkflowException.Permanent("payment.order.request_conflict");
        }

        if (attempt.Status != OrderPaymentStatus.Pending)
        {
            await PublishCurrentOutcomeAsync(attempt, eventPublisher, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var providerCustomerId = customer.ProviderCustomerId;
        if (string.IsNullOrWhiteSpace(providerCustomerId))
        {
            throw PaymentWorkflowException.Transient("payment.order.provider_customer_not_ready");
        }

        var method = await dbContext.PaymentMethods.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == attempt.PaymentMethodId &&
                    item.PaymentCustomerId == customer.Id &&
                    item.Status == PaymentMethodStatus.Active,
            cancellationToken);
        if (method is null)
        {
            await RejectAsync(attempt, "payment_method_unavailable", cancellationToken);
            return;
        }

        try
        {
            OrderPaymentProviderSession session;
            if (string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
            {
                session = await provider.CreateAsync(
                    attempt.OrderId,
                    providerCustomerId,
                    method.ProviderPaymentMethodId,
                    attempt.Amount,
                    attempt.CurrencyCode,
                    OrderPaymentProviderIdempotencyKeys.Create(attempt.OrderId),
                    cancellationToken);
                var assigned = attempt.AssignProviderPaymentIntent(
                    session.ProviderPaymentIntentId,
                    timeProvider.GetUtcNow());
                if (assigned.IsFailure)
                {
                    throw PaymentWorkflowException.Permanent(assigned.Error.Code);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                session = await provider.GetAsync(
                    attempt.ProviderPaymentIntentId,
                    cancellationToken);
            }

            if (string.Equals(session.Status, "requires_confirmation", StringComparison.Ordinal))
            {
                session = await provider.ConfirmAsync(
                    session.ProviderPaymentIntentId,
                    OrderPaymentProviderIdempotencyKeys.Confirm(attempt.OrderId),
                    cancellationToken);
            }

            ValidateProviderOwnership(
                session,
                providerCustomerId,
                method.ProviderPaymentMethodId,
                attempt);
            await ApplyProviderStateAsync(attempt, session, cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            throw exception.FailureKind == PaymentProviderFailureKind.Transient
                ? PaymentWorkflowException.Transient(exception.Code, exception)
                : PaymentWorkflowException.Permanent(exception.Code, exception);
        }
    }

    private static void ValidateProviderOwnership(
        OrderPaymentProviderSession session,
        string providerCustomerId,
        string providerPaymentMethodId,
        OrderPaymentAttempt attempt)
    {
        if (!string.Equals(session.ProviderCustomerId, providerCustomerId, StringComparison.Ordinal) ||
            !string.Equals(session.ProviderPaymentMethodId, providerPaymentMethodId, StringComparison.Ordinal) ||
            session.AmountMinor != ToMinorUnits(attempt.Amount) ||
            !string.Equals(session.CurrencyCode, attempt.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw PaymentWorkflowException.Permanent("payment.order.provider_state_mismatch");
        }
    }

    private async Task ApplyProviderStateAsync(
        OrderPaymentAttempt attempt,
        OrderPaymentProviderSession session,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        switch (session.Status)
        {
            case "requires_action":
                Ensure(attempt.RequireCustomerAction(now));
                await eventPublisher.PublishAsync(
                    new PaymentActionRequired(attempt.OrderId, attempt.Id, attempt.ExpiresAt, now),
                    cancellationToken: cancellationToken);
                break;
            case "requires_capture":
            case "succeeded":
                Ensure(attempt.Authorize(now));
                await eventPublisher.PublishAsync(
                    new PaymentAuthorized(
                        attempt.OrderId,
                        attempt.Id,
                        attempt.Amount,
                        attempt.CurrencyCode,
                        now),
                    cancellationToken: cancellationToken);
                break;
            case "requires_payment_method":
                Ensure(attempt.Reject("payment_method_rejected", now));
                await eventPublisher.PublishAsync(
                    new PaymentRejected(
                        attempt.OrderId,
                        attempt.Id,
                        "payment_method_rejected",
                        now),
                    cancellationToken: cancellationToken);
                break;
            case "canceled":
                Ensure(attempt.Cancel(now));
                await eventPublisher.PublishAsync(
                    new PaymentCancelled(attempt.OrderId, attempt.Id, now),
                    cancellationToken: cancellationToken);
                break;
            case "processing":
                break;
            default:
                throw PaymentWorkflowException.Transient("payment.order.provider_state_pending");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RejectAsync(
        OrderPaymentAttempt attempt,
        string code,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        Ensure(attempt.Reject(code, now));
        await eventPublisher.PublishAsync(
            new PaymentRejected(attempt.OrderId, attempt.Id, code, now),
            cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task PublishCurrentOutcomeAsync(
        OrderPaymentAttempt attempt,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        var occurredAt = attempt.UpdatedAt;
        switch (attempt.Status)
        {
            case OrderPaymentStatus.RequiresCustomerAction:
                await publisher.PublishAsync(
                    new PaymentActionRequired(
                        attempt.OrderId,
                        attempt.Id,
                        attempt.ExpiresAt,
                        occurredAt),
                    cancellationToken: cancellationToken);
                break;
            case OrderPaymentStatus.Authorized:
                await publisher.PublishAsync(
                    new PaymentAuthorized(
                        attempt.OrderId,
                        attempt.Id,
                        attempt.Amount,
                        attempt.CurrencyCode,
                        occurredAt),
                    cancellationToken: cancellationToken);
                break;
            case OrderPaymentStatus.Rejected:
                await publisher.PublishAsync(
                    new PaymentRejected(
                        attempt.OrderId,
                        attempt.Id,
                        attempt.RejectionCode ?? "rejected",
                        occurredAt),
                    cancellationToken: cancellationToken);
                break;
            case OrderPaymentStatus.Cancelled:
                await publisher.PublishAsync(
                    new PaymentCancelled(attempt.OrderId, attempt.Id, occurredAt),
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private static long ToMinorUnits(decimal amount) =>
        checked(decimal.ToInt64(amount * 100m));

    private static void Ensure(Result result)
    {
        if (result.IsFailure)
        {
            throw PaymentWorkflowException.Permanent(result.Error.Code);
        }
    }
}
