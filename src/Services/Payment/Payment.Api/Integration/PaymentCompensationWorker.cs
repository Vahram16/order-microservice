using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Integration;

internal sealed class PaymentCompensationWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<PaymentCompensationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 20;
    private static readonly Action<ILogger, Guid, string, Exception?> LogFailure = LoggerMessage.Define<Guid, string>(LogLevel.Error, new EventId(1, nameof(PaymentCompensationWorker)), "Payment compensation for order {OrderId} failed with {FailureCode}; the durable obligation remains pending.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);
            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var compensation = scope.ServiceProvider.GetRequiredService<OrderPaymentCompensationService>();
        var retryBefore = timeProvider.GetUtcNow() - RetryInterval;
        var attempts = await dbContext.OrderPaymentAttempts
            .Where(attempt => (attempt.Status == OrderPaymentStatus.CancellationRequested || attempt.Status == OrderPaymentStatus.RefundPending) && attempt.UpdatedAt <= retryBefore)
            .OrderBy(attempt => attempt.UpdatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var attempt in attempts)
        {
            try { await compensation.ReconcileAsync(attempt, cancellationToken); }
            catch (PaymentProviderException exception)
            {
                await PersistFailureAsync(compensation, attempt, exception.Code, cancellationToken);
                LogFailure(logger, attempt.OrderId, exception.Code, exception);
            }
            catch (PaymentWorkflowException exception)
            {
                await PersistFailureAsync(compensation, attempt, exception.Message, cancellationToken);
                LogFailure(logger, attempt.OrderId, exception.Message, exception);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                dbContext.ChangeTracker.Clear();
                LogFailure(logger, attempt.OrderId, "payment.order.compensation_concurrency", exception);
            }
        }
    }

    private static async Task PersistFailureAsync(OrderPaymentCompensationService compensation, OrderPaymentAttempt attempt, string failureCode, CancellationToken cancellationToken)
    {
        try { await compensation.RecordFailureAsync(attempt, failureCode, cancellationToken); }
        catch (DbUpdateConcurrencyException) { }
    }
}
