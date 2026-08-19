using System.Data;
using System.Data.Common;
using MassTransit;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Webhooks;

internal sealed class ProcessStripeWebhookConsumer(
    PaymentDbContext dbContext,
    IPaymentProvider provider,
    TimeProvider timeProvider) : IConsumer<ProcessStripeWebhook>
{
    public async Task Consume(ConsumeContext<ProcessStripeWebhook> context)
    {
        try
        {
            await ConsumeCoreAsync(context);
        }
        catch (WebhookProcessingException)
        {
            throw;
        }
        catch (PaymentProviderException exception)
        {
            throw ClassifyProviderFailure(exception);
        }
        catch (DbUpdateException exception)
        {
            throw WebhookProcessingException.Transient(
                "payment.webhook.persistence_failure",
                exception);
        }
        catch (DbException exception)
        {
            throw WebhookProcessingException.Transient(
                "payment.webhook.persistence_failure",
                exception);
        }
    }

    internal static Exception ClassifyProviderFailure(PaymentProviderException exception) =>
        exception.FailureKind == PaymentProviderFailureKind.Transient
            ? WebhookProcessingException.Transient(exception.Code, exception)
            : WebhookProcessingException.Permanent(exception.Code, exception);

    private async Task ConsumeCoreAsync(ConsumeContext<ProcessStripeWebhook> context)
    {
        var cancellationToken = context.CancellationToken;
        var webhookEvent = await dbContext.PaymentWebhookEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == context.Message.WebhookEventId,
                cancellationToken)
            ?? throw WebhookProcessingException.Permanent(
                "payment.webhook.event_not_found");

        if (webhookEvent.ProcessedAt is not null)
        {
            return;
        }

        var setupOperation = await dbContext.PaymentMethodSetupOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ProviderSetupIntentId == webhookEvent.ProviderSetupIntentId,
                cancellationToken)
            ?? throw WebhookProcessingException.Transient(
                "payment.webhook.setup_not_registered");

        var customer = await dbContext.PaymentCustomers
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == setupOperation.PaymentCustomerId,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(customer.ProviderCustomerId))
        {
            throw WebhookProcessingException.Permanent(
                "payment.webhook.provider_customer_missing");
        }

        var setup = await provider.GetPaymentMethodSetupAsync(
            webhookEvent.ProviderSetupIntentId,
            cancellationToken);

        if (!string.Equals(setup.Status, "succeeded", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(setup.ProviderPaymentMethodId) ||
            !string.Equals(
                setup.ProviderCustomerId,
                customer.ProviderCustomerId,
                StringComparison.Ordinal))
        {
            throw WebhookProcessingException.Permanent(
                "payment.webhook.setup_state_mismatch");
        }

        var providerMethod = await provider.GetPaymentMethodAsync(
            setup.ProviderPaymentMethodId,
            cancellationToken);

        if (!string.Equals(
                providerMethod.ProviderCustomerId,
                customer.ProviderCustomerId,
                StringComparison.Ordinal))
        {
            throw WebhookProcessingException.Permanent(
                "payment.webhook.method_ownership_mismatch");
        }

        await ReconcileAsync(
            webhookEvent.Id,
            customer.Id,
            providerMethod,
            cancellationToken);
    }

    private async Task ReconcileAsync(
        Guid webhookEventId,
        Guid paymentCustomerId,
        ProviderPaymentMethod providerMethod,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Each execution-strategy attempt must start from database truth. This consumer explicitly
            // opts out of the EF consumer outbox, so clearing only this reconciliation context cannot
            // detach MassTransit inbox/outbox state.
            dbContext.ChangeTracker.Clear();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            // A command may be redelivered after the database commit but before the broker ACK.
            // The durable receipt is the idempotency fence; no service-owned lease/token protocol is needed.
            var webhookEvent = await dbContext.PaymentWebhookEvents
                .FromSqlInterpolated($"SELECT * FROM payment_webhook_events WHERE \"Id\" = {webhookEventId} FOR UPDATE")
                .SingleAsync(cancellationToken);

            if (webhookEvent.ProcessedAt is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var method = await dbContext.PaymentMethods.SingleOrDefaultAsync(
                item => item.ProviderPaymentMethodId == providerMethod.ProviderPaymentMethodId,
                cancellationToken);

            var card = new CardPaymentMethodDetails(
                providerMethod.Brand,
                providerMethod.Last4,
                providerMethod.ExpMonth,
                providerMethod.ExpYear,
                providerMethod.WalletType);
            var now = timeProvider.GetUtcNow();

            if (method is null)
            {
                var hasDefault = await dbContext.PaymentMethods.AnyAsync(
                    item =>
                        item.PaymentCustomerId == paymentCustomerId &&
                        item.IsDefault &&
                        item.Status == PaymentMethodStatus.Active,
                    cancellationToken);

                var created = PaymentMethod.Create(
                    Guid.NewGuid(),
                    paymentCustomerId,
                    providerMethod.ProviderPaymentMethodId,
                    card,
                    isDefault: !hasDefault,
                    now);
                if (created.IsFailure)
                {
                    throw WebhookProcessingException.Permanent(created.Error.Code);
                }

                dbContext.PaymentMethods.Add(created.Value);
            }
            else
            {
                if (method.PaymentCustomerId != paymentCustomerId)
                {
                    throw WebhookProcessingException.Permanent(
                        "payment.webhook.method_customer_conflict");
                }

                var synchronized = method.Synchronize(card, now);
                if (synchronized.IsFailure)
                {
                    throw WebhookProcessingException.Permanent(synchronized.Error.Code);
                }

                var hasDefault = await dbContext.PaymentMethods.AnyAsync(
                    item =>
                        item.PaymentCustomerId == paymentCustomerId &&
                        item.Id != method.Id &&
                        item.IsDefault &&
                        item.Status == PaymentMethodStatus.Active,
                    cancellationToken);
                if (!hasDefault)
                {
                    var makeDefault = method.MakeDefault(now);
                    if (makeDefault.IsFailure)
                    {
                        throw WebhookProcessingException.Permanent(makeDefault.Error.Code);
                    }
                }
            }

            webhookEvent.MarkProcessed(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private abstract class WebhookProcessingException(
        string code,
        Exception? innerException = null) : Exception(code, innerException)
    {
        public string Code { get; } = code;

        public static WebhookProcessingException Permanent(
            string code,
            Exception? innerException = null) =>
            new PermanentWebhookProcessingException(code, innerException);

        public static WebhookProcessingException Transient(
            string code,
            Exception? innerException = null) =>
            new TransientWebhookProcessingException(code, innerException);

        private sealed class PermanentWebhookProcessingException(
            string code,
            Exception? innerException)
            : WebhookProcessingException(code, innerException), IPermanentConsumerFailure;

        private sealed class TransientWebhookProcessingException(
            string code,
            Exception? innerException)
            : WebhookProcessingException(code, innerException), ITransientConsumerFailure;
    }
}
