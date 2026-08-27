using System.Data;
using System.Data.Common;
using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Payments.V1;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Webhooks;

internal sealed class ProcessStripeWebhookConsumer(
    PaymentDbContext dbContext,
    IPaymentProvider paymentMethodProvider,
    IOrderPaymentProvider orderPaymentProvider,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<ProcessStripeWebhook>
{
    public async Task Consume(ConsumeContext<ProcessStripeWebhook> context)
    {
        try { await ConsumeCoreAsync(context); }
        catch (WebhookProcessingException) { throw; }
        catch (PaymentProviderException exception) { throw ClassifyProviderFailure(exception); }
        catch (DbUpdateException exception) { throw WebhookProcessingException.Transient("payment.webhook.persistence_failure", exception); }
        catch (DbException exception) { throw WebhookProcessingException.Transient("payment.webhook.persistence_failure", exception); }
    }

    internal static Exception ClassifyProviderFailure(PaymentProviderException exception) =>
        exception.FailureKind == PaymentProviderFailureKind.Transient
            ? WebhookProcessingException.Transient(exception.Code, exception)
            : WebhookProcessingException.Permanent(exception.Code, exception);

    private async Task ConsumeCoreAsync(ConsumeContext<ProcessStripeWebhook> context)
    {
        var cancellationToken = context.CancellationToken;
        var webhookEvent = await dbContext.PaymentWebhookEvents.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == context.Message.WebhookEventId,
            cancellationToken) ?? throw WebhookProcessingException.Permanent("payment.webhook.event_not_found");
        if (webhookEvent.ProcessedAt is not null) return;

        if (!string.IsNullOrWhiteSpace(webhookEvent.ProviderSetupIntentId))
        {
            await ProcessPaymentMethodSetupAsync(webhookEvent, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(webhookEvent.ProviderPaymentIntentId))
        {
            await ProcessOrderPaymentAsync(webhookEvent, cancellationToken);
            return;
        }

        throw WebhookProcessingException.Permanent("payment.webhook.object_missing");
    }

    private async Task ProcessPaymentMethodSetupAsync(PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var setupOperation = await dbContext.PaymentMethodSetupOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProviderSetupIntentId == webhookEvent.ProviderSetupIntentId,
            cancellationToken) ?? throw WebhookProcessingException.Transient("payment.webhook.setup_not_registered");
        var customer = await dbContext.PaymentCustomers.AsNoTracking().SingleAsync(item => item.Id == setupOperation.PaymentCustomerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(customer.ProviderCustomerId)) throw WebhookProcessingException.Permanent("payment.webhook.provider_customer_missing");

        var setup = await paymentMethodProvider.GetPaymentMethodSetupAsync(webhookEvent.ProviderSetupIntentId!, cancellationToken);
        if (!string.Equals(setup.Status, "succeeded", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(setup.ProviderPaymentMethodId) ||
            !string.Equals(setup.ProviderCustomerId, customer.ProviderCustomerId, StringComparison.Ordinal))
        {
            throw WebhookProcessingException.Permanent("payment.webhook.setup_state_mismatch");
        }

        var providerMethod = await paymentMethodProvider.GetPaymentMethodAsync(setup.ProviderPaymentMethodId, cancellationToken);
        if (!string.Equals(providerMethod.ProviderCustomerId, customer.ProviderCustomerId, StringComparison.Ordinal))
        {
            throw WebhookProcessingException.Permanent("payment.webhook.method_ownership_mismatch");
        }

        await ReconcilePaymentMethodAsync(webhookEvent.Id, customer.Id, providerMethod, cancellationToken);
    }

    private async Task ProcessOrderPaymentAsync(PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.OrderPaymentAttempts.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProviderPaymentIntentId == webhookEvent.ProviderPaymentIntentId,
            cancellationToken) ?? throw WebhookProcessingException.Transient("payment.webhook.order_payment_not_registered");
        var customer = await dbContext.PaymentCustomers.AsNoTracking().SingleAsync(item => item.Id == attempt.PaymentCustomerId, cancellationToken);
        var method = await dbContext.PaymentMethods.AsNoTracking().SingleAsync(item => item.Id == attempt.PaymentMethodId, cancellationToken);
        if (string.IsNullOrWhiteSpace(customer.ProviderCustomerId)) throw WebhookProcessingException.Permanent("payment.webhook.provider_customer_missing");

        var session = await orderPaymentProvider.GetAsync(webhookEvent.ProviderPaymentIntentId!, cancellationToken);
        ValidateOrderPaymentSession(session, attempt, customer.ProviderCustomerId, method.ProviderPaymentMethodId);

        if (attempt.Status is OrderPaymentStatus.Cancelled or OrderPaymentStatus.Rejected &&
            string.Equals(session.Status, "requires_capture", StringComparison.Ordinal))
        {
            session = await orderPaymentProvider.CancelAsync(
                session.ProviderPaymentIntentId,
                OrderPaymentProviderIdempotencyKeys.Cancel(attempt.OrderId),
                cancellationToken);
        }

        if (attempt.Status is OrderPaymentStatus.Cancelled or OrderPaymentStatus.Rejected &&
            string.Equals(session.Status, "succeeded", StringComparison.Ordinal))
        {
            throw WebhookProcessingException.Permanent("payment.webhook.late_captured_payment_requires_manual_reconciliation");
        }

        await ReconcileOrderPaymentAsync(webhookEvent.Id, attempt.Id, session, cancellationToken);
    }

    private async Task ReconcilePaymentMethodAsync(Guid webhookEventId, Guid paymentCustomerId, ProviderPaymentMethod providerMethod, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var webhookEvent = await dbContext.PaymentWebhookEvents.FromSqlInterpolated($"SELECT * FROM payment_webhook_events WHERE \"Id\" = {webhookEventId} FOR UPDATE").SingleAsync(cancellationToken);
            if (webhookEvent.ProcessedAt is not null) { await transaction.CommitAsync(cancellationToken); return; }

            var method = await dbContext.PaymentMethods.SingleOrDefaultAsync(item => item.ProviderPaymentMethodId == providerMethod.ProviderPaymentMethodId, cancellationToken);
            var card = new CardPaymentMethodDetails(providerMethod.Brand, providerMethod.Last4, providerMethod.ExpMonth, providerMethod.ExpYear, providerMethod.WalletType);
            var now = timeProvider.GetUtcNow();
            if (method is null)
            {
                var hasDefault = await dbContext.PaymentMethods.AnyAsync(item => item.PaymentCustomerId == paymentCustomerId && item.IsDefault && item.Status == PaymentMethodStatus.Active, cancellationToken);
                var created = PaymentMethod.Create(Guid.NewGuid(), paymentCustomerId, providerMethod.ProviderPaymentMethodId, card, !hasDefault, now);
                if (created.IsFailure) throw WebhookProcessingException.Permanent(created.Error.Code);
                dbContext.PaymentMethods.Add(created.Value);
            }
            else
            {
                if (method.PaymentCustomerId != paymentCustomerId) throw WebhookProcessingException.Permanent("payment.webhook.method_customer_conflict");
                var synchronized = method.Synchronize(card, now);
                if (synchronized.IsFailure) throw WebhookProcessingException.Permanent(synchronized.Error.Code);
                var hasDefault = await dbContext.PaymentMethods.AnyAsync(item => item.PaymentCustomerId == paymentCustomerId && item.Id != method.Id && item.IsDefault && item.Status == PaymentMethodStatus.Active, cancellationToken);
                if (!hasDefault)
                {
                    var makeDefault = method.MakeDefault(now);
                    if (makeDefault.IsFailure) throw WebhookProcessingException.Permanent(makeDefault.Error.Code);
                }
            }

            webhookEvent.MarkProcessed(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task ReconcileOrderPaymentAsync(Guid webhookEventId, Guid attemptId, OrderPaymentProviderSession session, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var webhookEvent = await dbContext.PaymentWebhookEvents.FromSqlInterpolated($"SELECT * FROM payment_webhook_events WHERE \"Id\" = {webhookEventId} FOR UPDATE").SingleAsync(cancellationToken);
            if (webhookEvent.ProcessedAt is not null) { await transaction.CommitAsync(cancellationToken); return; }
            var attempt = await dbContext.OrderPaymentAttempts.SingleAsync(item => item.Id == attemptId, cancellationToken);
            var now = timeProvider.GetUtcNow();

            switch (session.Status)
            {
                case "requires_action":
                    Ensure(attempt.RequireCustomerAction(now));
                    await eventPublisher.PublishAsync(new PaymentActionRequired(attempt.OrderId, attempt.Id, attempt.ExpiresAt, now), cancellationToken: cancellationToken);
                    break;
                case "requires_capture":
                case "succeeded":
                    Ensure(attempt.Authorize(now));
                    await eventPublisher.PublishAsync(new PaymentAuthorized(attempt.OrderId, attempt.Id, attempt.Amount, attempt.CurrencyCode, now), cancellationToken: cancellationToken);
                    break;
                case "requires_payment_method":
                    Ensure(attempt.Reject("payment_method_rejected", now));
                    await eventPublisher.PublishAsync(new PaymentRejected(attempt.OrderId, attempt.Id, "payment_method_rejected", now), cancellationToken: cancellationToken);
                    break;
                case "canceled":
                    Ensure(attempt.Cancel(now));
                    await eventPublisher.PublishAsync(new PaymentCancelled(attempt.OrderId, attempt.Id, now), cancellationToken: cancellationToken);
                    break;
                case "processing":
                case "requires_confirmation":
                    break;
                default:
                    throw WebhookProcessingException.Permanent("payment.webhook.unknown_order_payment_state");
            }

            webhookEvent.MarkProcessed(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static void ValidateOrderPaymentSession(OrderPaymentProviderSession session, OrderPaymentAttempt attempt, string providerCustomerId, string providerPaymentMethodId)
    {
        if (!string.Equals(session.ProviderPaymentIntentId, attempt.ProviderPaymentIntentId, StringComparison.Ordinal) ||
            !string.Equals(session.ProviderCustomerId, providerCustomerId, StringComparison.Ordinal) ||
            !string.Equals(session.ProviderPaymentMethodId, providerPaymentMethodId, StringComparison.Ordinal) ||
            session.AmountMinor != checked(decimal.ToInt64(attempt.Amount * 100m)) ||
            !string.Equals(session.CurrencyCode, attempt.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw WebhookProcessingException.Permanent("payment.webhook.order_payment_state_mismatch");
        }
    }

    private static void Ensure(Result result)
    {
        if (result.IsFailure) throw WebhookProcessingException.Permanent(result.Error.Code);
    }

    private abstract class WebhookProcessingException(string code, Exception? innerException = null) : Exception(code, innerException)
    {
        public static WebhookProcessingException Permanent(string code, Exception? innerException = null) => new PermanentFailure(code, innerException);
        public static WebhookProcessingException Transient(string code, Exception? innerException = null) => new TransientFailure(code, innerException);
        private sealed class PermanentFailure(string code, Exception? innerException) : WebhookProcessingException(code, innerException), IPermanentConsumerFailure;
        private sealed class TransientFailure(string code, Exception? innerException) : WebhookProcessingException(code, innerException), ITransientConsumerFailure;
    }
}
