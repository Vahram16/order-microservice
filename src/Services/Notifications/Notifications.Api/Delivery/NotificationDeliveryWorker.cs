using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;

namespace Notifications.Api.Delivery;

internal sealed partial class NotificationDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveryWorker> logger)
    : BackgroundService
{
    private readonly NotificationDeliveryOptions _options = options.Value;
    private DateTimeOffset _nextCleanupAtUtc = timeProvider.GetUtcNow();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(
            TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1500)),
            timeProvider,
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                var cleanup = now >= _nextCleanupAtUtc;
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<NotificationDeliveryDispatcher>();
                await dispatcher.DispatchBatchAsync(cleanup, stoppingToken);
                if (cleanup)
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
                LogCycleFailed(logger, exception);
            }

            await Task.Delay(_options.DispatchInterval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(
        EventId = 2110,
        Level = LogLevel.Error,
        Message = "The notification delivery cycle failed")]
    private static partial void LogCycleFailed(
        ILogger logger,
        Exception exception);
}
