using Identity.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

internal sealed partial class IdentityNotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityNotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityNotificationOutboxWorker> logger)
    : BackgroundService
{
    private readonly IdentityNotificationOptions _options = options.Value;
    private DateTimeOffset _nextCleanupAtUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialJitter = TimeSpan.FromMilliseconds(
            Random.Shared.NextDouble() * _options.DispatchInterval.TotalMilliseconds);
        await Task.Delay(initialJitter, timeProvider, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                var cleanupExpiredRecords = now >= _nextCleanupAtUtc;

                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<IdentityNotificationOutboxDispatcher>();
                await dispatcher.DispatchBatchAsync(
                    cleanupExpiredRecords,
                    stoppingToken);

                if (cleanupExpiredRecords)
                {
                    _nextCleanupAtUtc = now + TimeSpan.FromHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDispatchCycleFailed(logger, exception);
            }

            await Task.Delay(_options.DispatchInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(
        EventId = 1013,
        Level = LogLevel.Error,
        Message = "Identity notification dispatch cycle failed")]
    private static partial void LogDispatchCycleFailed(
        ILogger logger,
        Exception exception);
}
