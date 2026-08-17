using System.Data;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Webhooks;

internal sealed class StripeWebhookProcessor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<StripeWebhookProcessor> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaximumAttempts = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);
    private static readonly Action<ILogger, Guid, string, Exception?> ProcessingFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2401, nameof(ProcessingFailed)),
            "Payment webhook {WebhookEventId} failed with {FailureCode}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        do
        {
            await ProcessBatchAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var now = timeProvider.GetUtcNow();

        var ids = await dbContext.PaymentWebhookEvents
            .AsNoTracking()
            .Where(item =>
                item.ProcessedAt == null &&
                item.DeadLetteredAt == null &&
                item.NextAttemptAt <= now &&
                (item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now))
            .OrderBy(item => item.ReceivedAt)
            .Select(item => item.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            await ProcessOneAsync(id, cancellationToken);
        }
    }

    private async Task ProcessOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();

        using (var claimScope = scopeFactory.CreateScope())
        {
            var claimDb = claimScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var claimed = await claimDb.PaymentWebhookEvents
                .Where(item =>
                    item.Id == id &&
                    item.ProcessedAt == null &&
                    item.DeadLetteredAt == null &&
                    item.NextAttemptAt <= now &&
                    (item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.ProcessingToken, token)
                        .SetProperty(item => item.LeaseExpiresAt, now.Add(ProcessingLease)),
                    cancellationToken);

            if (claimed == 0)
            {
                return;
            }
        }

        try
        {
            await ReconcileAsync(id, token, cancellationToken);
        }
        catch (WebhookProcessingException exception)
        {
            ProcessingFailed(logger, id, exception.Code, exception);
            await MarkFailedAsync(
                id,
                token,
                exception.Code,
                exception.IsPermanent ? 1 : MaximumAttempts,
                cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            ProcessingFailed(logger, id, exception.Code, exception);
            await MarkFailedAsync(id, token, exception.Code, MaximumAttempts, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            const string code = "payment.webhook.unexpected_failure";
            ProcessingFailed(logger, id, code, exception);
            await MarkFailedAsync(id, token, code, MaximumAttempts, cancellationToken);
        }
    }

    private async Task ReconcileAsync(
        Guid eventId,
        Guid processingToken,
        CancellationToken cancellationToken)
    {
        using var providerScope = scopeFactory.CreateScope();
        var readDb = providerScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var provider = providerScope.ServiceProvider.GetRequiredService<IPaymentProvider>();

        var webhookEvent = await readDb.PaymentWebhookEvents
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == eventId && item.ProcessingToken == processingToken,
                cancellationToken);

        var setupOperation = await readDb.PaymentMethodSetupOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ProviderSetupIntentId == webhookEvent.ProviderSetupIntentId,
                cancellationToken)
            ?? throw WebhookProcessingException.Transient("payment.webhook.setup_not_registered");

        var customer = await readDb.PaymentCustomers
            .AsNoTracking()
            .SingleAsync(item => item.Id == setupOperation.PaymentCustomerId, cancellationToken);

        if (string.IsNullOrWhiteSpace(customer.ProviderCustomerId))
        {
            throw WebhookProcessingException.Permanent("payment.webhook.provider_customer_missing");
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
            throw WebhookProcessingException.Permanent("payment.webhook.setup_state_mismatch");
        }

        var providerMethod = await provider.GetPaymentMethodAsync(
            setup.ProviderPaymentMethodId,
            cancellationToken);

        if (!string.Equals(
                providerMethod.ProviderCustomerId,
                customer.ProviderCustomerId,
                StringComparison.Ordinal))
        {
            throw WebhookProcessingException.Permanent("payment.webhook.method_ownership_mismatch");
        }

        using var writeScope = scopeFactory.CreateScope();
        var dbContext = writeScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var leasedEvent = await dbContext.PaymentWebhookEvents.SingleAsync(
            item => item.Id == eventId && item.ProcessingToken == processingToken,
            cancellationToken);

        var method = await dbContext.PaymentMethods.SingleOrDefaultAsync(
            item => item.ProviderPaymentMethodId == providerMethod.ProviderPaymentMethodId,
            cancellationToken);

        var card = new CardPaymentMethodDetails(
            providerMethod.Brand,
            providerMethod.Last4,
            providerMethod.ExpMonth,
            providerMethod.ExpYear,
            providerMethod.WalletType);
        var currentTime = timeProvider.GetUtcNow();

        if (method is null)
        {
            var hasDefault = await dbContext.PaymentMethods.AnyAsync(
                item =>
                    item.PaymentCustomerId == customer.Id &&
                    item.IsDefault &&
                    item.Status == PaymentMethodStatus.Active,
                cancellationToken);

            var created = PaymentMethod.Create(
                Guid.NewGuid(),
                customer.Id,
                providerMethod.ProviderPaymentMethodId,
                card,
                isDefault: !hasDefault,
                currentTime);
            if (created.IsFailure)
            {
                throw WebhookProcessingException.Permanent(created.Error.Code);
            }

            dbContext.PaymentMethods.Add(created.Value);
        }
        else
        {
            if (method.PaymentCustomerId != customer.Id)
            {
                throw WebhookProcessingException.Permanent(
                    "payment.webhook.method_customer_conflict");
            }

            var synchronized = method.Synchronize(card, currentTime);
            if (synchronized.IsFailure)
            {
                throw WebhookProcessingException.Permanent(synchronized.Error.Code);
            }

            var hasDefault = await dbContext.PaymentMethods.AnyAsync(
                item =>
                    item.PaymentCustomerId == customer.Id &&
                    item.Id != method.Id &&
                    item.IsDefault &&
                    item.Status == PaymentMethodStatus.Active,
                cancellationToken);
            if (!hasDefault)
            {
                var makeDefault = method.MakeDefault(currentTime);
                if (makeDefault.IsFailure)
                {
                    throw WebhookProcessingException.Permanent(makeDefault.Error.Code);
                }
            }
        }

        leasedEvent.MarkProcessed(currentTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        Guid eventId,
        Guid token,
        string errorCode,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var item = await dbContext.PaymentWebhookEvents.SingleOrDefaultAsync(
            webhookEvent =>
                webhookEvent.Id == eventId &&
                webhookEvent.ProcessingToken == token &&
                webhookEvent.ProcessedAt == null,
            cancellationToken);

        if (item is null)
        {
            return;
        }

        item.MarkFailed(timeProvider.GetUtcNow(), errorCode, maximumAttempts);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class WebhookProcessingException(
        string code,
        bool isPermanent) : Exception(code)
    {
        public string Code { get; } = code;
        public bool IsPermanent { get; } = isPermanent;

        public static WebhookProcessingException Permanent(string code) => new(code, true);
        public static WebhookProcessingException Transient(string code) => new(code, false);
    }
}
