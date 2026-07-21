using System.Data;
using Identity.Api.Configuration;
using Identity.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Identity.Api.Maintenance;

internal sealed partial class OpenIddictMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityMaintenanceOptions> options,
    TimeProvider timeProvider,
    ILogger<OpenIddictMaintenanceService> logger)
    : BackgroundService
{
    private const long AdvisoryLockId = 0x4944454E544D4149;
    private readonly IdentityMaintenanceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = _options.PruneInterval;
            try
            {
                if (!await TryPruneAsync(stoppingToken))
                {
                    LogMaintenanceSkipped(logger);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                delay = _options.FailureRetryInterval;
                LogMaintenanceFailed(logger, delay, exception);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<bool> TryPruneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityServiceDbContext>();
        var pruning = scope.ServiceProvider.GetRequiredService<OpenIddictPruningOperation>();
        var strategy = database.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            if (!await TryAcquireMaintenanceLockAsync(
                    database,
                    transaction,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var threshold = timeProvider.GetUtcNow() - _options.MinimumAge;
            var result = await pruning.ExecuteAsync(threshold, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogMaintenanceCompleted(
                logger,
                result.Tokens,
                result.Authorizations,
                threshold);
            return true;
        });
    }

    private static async Task<bool> TryAcquireMaintenanceLockAsync(
        IdentityServiceDbContext database,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_try_advisory_xact_lock(@lock_id)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_id";
        parameter.Value = AdvisoryLockId;
        command.Parameters.Add(parameter);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Information,
        Message = "OpenIddict maintenance pruned {TokenCount} tokens and {AuthorizationCount} authorizations created before {ThresholdUtc}")]
    private static partial void LogMaintenanceCompleted(
        ILogger logger,
        long tokenCount,
        long authorizationCount,
        DateTimeOffset thresholdUtc);

    [LoggerMessage(
        EventId = 1401,
        Level = LogLevel.Debug,
        Message = "OpenIddict maintenance skipped because another replica holds the advisory lock")]
    private static partial void LogMaintenanceSkipped(ILogger logger);

    [LoggerMessage(
        EventId = 1402,
        Level = LogLevel.Error,
        Message = "OpenIddict maintenance failed and will retry after {RetryInterval}")]
    private static partial void LogMaintenanceFailed(
        ILogger logger,
        TimeSpan retryInterval,
        Exception exception);
}
