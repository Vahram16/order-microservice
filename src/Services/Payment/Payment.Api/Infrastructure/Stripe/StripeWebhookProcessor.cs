using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Persistence;

namespace Payment.Api.Infrastructure.Stripe;

internal sealed class StripeWebhookProcessor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<StripeWebhookProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(15);
    private static readonly Action<ILogger, Exception?> LogIterationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1, "StripeWebhookProcessorIterationFailed"),
        "Stripe webhook processor iteration failed.");
    private static readonly Action<ILogger, string, Exception?> LogWebhookFailed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(2, "StripeWebhookProcessingFailed"),
        "Stripe webhook {EventId} processing failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await TryClaimAsync(stoppingToken);
                if (claimed is null)
                {
                    await Task.Delay(PollInterval, timeProvider, stoppingToken);
                    continue;
                }

                await ProcessAsync(claimed.Value.EventId, claimed.Value.LeaseId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogIterationFailed(logger, exception);
                await Task.Delay(PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task<(string EventId, Guid LeaseId)?> TryClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var now = timeProvider.GetUtcNow();

        var eventId = await dbContext.StripeWebhookInbox
            .AsNoTracking()
            .Where(entry => entry.ProcessedAtUtc == null &&
                            (entry.NextAttemptAtUtc == null || entry.NextAttemptAtUtc <= now) &&
                            (entry.ProcessingLeaseExpiresAtUtc == null || entry.ProcessingLeaseExpiresAtUtc <= now))
            .OrderBy(entry => entry.ReceivedAtUtc)
            .Select(entry => entry.EventId)
            .FirstOrDefaultAsync(cancellationToken);
        if (eventId is null)
        {
            return null;
        }

        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now.Add(ProcessingLease);
        var affected = await dbContext.StripeWebhookInbox
            .Where(entry => entry.EventId == eventId &&
                            entry.ProcessedAtUtc == null &&
                            (entry.ProcessingLeaseExpiresAtUtc == null || entry.ProcessingLeaseExpiresAtUtc <= now))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entry => entry.ProcessingLeaseId, leaseId)
                    .SetProperty(entry => entry.ProcessingLeaseExpiresAtUtc, leaseExpiresAt)
                    .SetProperty(entry => entry.AttemptCount, entry => entry.AttemptCount + 1),
                cancellationToken);

        return affected == 1 ? (eventId, leaseId) : null;
    }

    private async Task ProcessAsync(string eventId, Guid leaseId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var stripeGateway = scope.ServiceProvider.GetRequiredService<IStripeGateway>();

            var inbox = await dbContext.StripeWebhookInbox.AsNoTracking().SingleAsync(
                entry => entry.EventId == eventId && entry.ProcessingLeaseId == leaseId,
                cancellationToken);

            var setupIntent = await stripeGateway.GetSetupIntentAsync(inbox.ObjectId, cancellationToken);
            if (!string.Equals(setupIntent.Status, "succeeded", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(setupIntent.PaymentMethodId) ||
                string.IsNullOrWhiteSpace(setupIntent.CustomerId))
            {
                throw new InvalidOperationException(
                    "The Stripe SetupIntent is not in a reconcilable succeeded state.");
            }

            if (!setupIntent.Metadata.TryGetValue("customer_id", out var customerIdValue) ||
                !Guid.TryParse(customerIdValue, out var customerId) ||
                customerId == Guid.Empty)
            {
                throw new InvalidOperationException("Stripe SetupIntent customer metadata is invalid.");
            }

            var makeDefault = setupIntent.Metadata.TryGetValue("make_default", out var makeDefaultValue) &&
                              bool.TryParse(makeDefaultValue, out var parsedMakeDefault) &&
                              parsedMakeDefault;

            var providerMethod = await stripeGateway.GetPaymentMethodAsync(
                setupIntent.PaymentMethodId,
                cancellationToken);

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var paymentCustomer = await dbContext.PaymentCustomers.SingleAsync(
                customer => customer.CustomerId == customerId,
                cancellationToken);
            if (!string.Equals(
                    paymentCustomer.StripeCustomerId,
                    setupIntent.CustomerId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stripe SetupIntent customer does not match the local payment customer.");
            }

            var method = await dbContext.PaymentMethods.SingleOrDefaultAsync(
                candidate => candidate.ProviderPaymentMethodId == providerMethod.Id,
                cancellationToken);
            var now = timeProvider.GetUtcNow();
            var hasDefault = await dbContext.PaymentMethods.AnyAsync(
                candidate => candidate.CustomerId == customerId &&
                             candidate.IsDefault &&
                             candidate.Status == SavedPaymentMethodStatus.Active,
                cancellationToken);
            var shouldBeDefault = makeDefault || !hasDefault;

            if (shouldBeDefault)
            {
                var currentDefaults = await dbContext.PaymentMethods
                    .Where(candidate => candidate.CustomerId == customerId && candidate.IsDefault)
                    .ToListAsync(cancellationToken);
                foreach (var currentDefault in currentDefaults)
                {
                    currentDefault.ClearDefault(now);
                }
            }

            if (method is null)
            {
                method = SavedPaymentMethod.Create(
                    customerId,
                    providerMethod.Id,
                    providerMethod.Type,
                    providerMethod.Brand,
                    providerMethod.Last4,
                    providerMethod.ExpMonth,
                    providerMethod.ExpYear,
                    providerMethod.WalletType,
                    shouldBeDefault,
                    now);
                dbContext.PaymentMethods.Add(method);
            }
            else
            {
                if (method.CustomerId != customerId)
                {
                    throw new InvalidOperationException(
                        "A Stripe payment method cannot be reassigned to another payment customer.");
                }

                method.Synchronize(
                    providerMethod.Type,
                    providerMethod.Brand,
                    providerMethod.Last4,
                    providerMethod.ExpMonth,
                    providerMethod.ExpYear,
                    providerMethod.WalletType,
                    now);
                if (shouldBeDefault)
                {
                    method.MakeDefault(now);
                }
            }

            var trackedInbox = await dbContext.StripeWebhookInbox.SingleAsync(
                entry => entry.EventId == eventId && entry.ProcessingLeaseId == leaseId,
                cancellationToken);
            trackedInbox.ProcessedAtUtc = now;
            trackedInbox.ProcessingLeaseId = null;
            trackedInbox.ProcessingLeaseExpiresAtUtc = null;
            trackedInbox.NextAttemptAtUtc = null;
            trackedInbox.LastError = null;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(eventId, leaseId, exception, cancellationToken);
            LogWebhookFailed(logger, eventId, exception);
        }
    }

    private async Task RecordFailureAsync(
        string eventId,
        Guid leaseId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var nextAttempt = timeProvider.GetUtcNow().Add(FailureBackoff);
        var message = exception.Message.Length <= 2000
            ? exception.Message
            : exception.Message[..2000];

        await dbContext.StripeWebhookInbox
            .Where(entry => entry.EventId == eventId && entry.ProcessingLeaseId == leaseId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entry => entry.ProcessingLeaseId, (Guid?)null)
                    .SetProperty(entry => entry.ProcessingLeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(entry => entry.NextAttemptAtUtc, nextAttempt)
                    .SetProperty(entry => entry.LastError, message),
                cancellationToken);
    }
}
