using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microservices.Messaging;

internal static class MessagingInstrumentation
{
    public const string MeterName = "Microservices.Messaging";

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "messaging.consumer.retry.attempts",
        description: "Immediate consumer retry attempts.");

    public static readonly Counter<long> RedeliveryAttempts = Meter.CreateCounter<long>(
        "messaging.consumer.redelivery.attempts",
        description: "Broker-backed consumer redelivery attempts.");

    public static readonly Counter<long> ConsumerFailures = Meter.CreateCounter<long>(
        "messaging.consumer.failures",
        description: "Consumer attempts that ended with an exception.");

    public static readonly Histogram<double> ConsumerAttemptDuration = Meter.CreateHistogram<double>(
        "messaging.consumer.attempt.duration",
        unit: "s",
        description: "Duration of each consumer delivery attempt, including retries.");

    public static readonly Counter<long> OutboxCollectionFailures = Meter.CreateCounter<long>(
        "messaging.outbox.collection.failures",
        description: "Failures while collecting PostgreSQL outbox backlog metrics.");
}

internal sealed class ConsumerDeliveryMetricsFilter<T>(
    IConsumerExceptionClassifier classifier) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var endpoint = context.ReceiveContext.InputAddress.AbsolutePath.Trim('/');
        var retryAttempt = context.GetRetryAttempt();
        var redeliveryCount = context.GetRedeliveryCount();
        var tags = new TagList
        {
            { "messaging.destination.name", endpoint },
            { "messaging.message.type", typeof(T).FullName ?? typeof(T).Name }
        };

        if (retryAttempt > 0)
        {
            MessagingInstrumentation.RetryAttempts.Add(1, tags);
        }
        else if (redeliveryCount > 0)
        {
            MessagingInstrumentation.RedeliveryAttempts.Add(1, tags);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next.Send(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            tags.Add("error.type", exception.GetType().FullName ?? exception.GetType().Name);
            tags.Add(
                "messaging.failure.disposition",
                classifier.Classify(exception).ToString().ToLowerInvariant());
            MessagingInstrumentation.ConsumerFailures.Add(1, tags);
            throw;
        }
        finally
        {
            MessagingInstrumentation.ConsumerAttemptDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                tags);
        }
    }

    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("consumerDeliveryMetrics");
}

internal sealed partial class OutboxMetricsCollector<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxMetricsCollector<TDbContext>> _logger;
    private readonly TimeSpan _interval;
    private readonly KeyValuePair<string, object?>[] _tags;
    private readonly ObservableGauge<long> _backlogGauge;
    private readonly ObservableGauge<double> _oldestAgeGauge;
    private long _backlog;
    private double _oldestAgeSeconds;
    private int _hasSnapshot;

    public OutboxMetricsCollector(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxMetricsCollector<TDbContext>> logger,
        RabbitMqMessagingOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = options.OutboxMetricsInterval;
        _tags = [new KeyValuePair<string, object?>("db.context", typeof(TDbContext).Name)];
        _backlogGauge = MessagingInstrumentation.Meter.CreateObservableGauge(
            "messaging.outbox.backlog",
            ObserveBacklog,
            description: "Messages waiting in the PostgreSQL bus or consumer outbox.");
        _oldestAgeGauge = MessagingInstrumentation.Meter.CreateObservableGauge(
            "messaging.outbox.oldest.age",
            ObserveOldestAge,
            unit: "s",
            description: "Age of the oldest message waiting in the PostgreSQL outbox.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await CollectAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
            var outboxMessages = dbContext.Set<OutboxMessage>().AsNoTracking();
            var backlog = await outboxMessages.LongCountAsync(cancellationToken).ConfigureAwait(false);
            var oldestSentTime = await outboxMessages
                .Select(message => (DateTime?)message.SentTime)
                .MinAsync(cancellationToken)
                .ConfigureAwait(false);

            Interlocked.Exchange(ref _backlog, backlog);
            Volatile.Write(
                ref _oldestAgeSeconds,
                oldestSentTime is null
                    ? 0
                    : Math.Max(0, (DateTime.UtcNow - oldestSentTime.Value).TotalSeconds));
            Volatile.Write(ref _hasSnapshot, 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Keep the last successful sample. Before the first successful query, emit no gauge
            // sample rather than reporting a misleading zero backlog.
            MessagingInstrumentation.OutboxCollectionFailures.Add(1, _tags);
            LogCollectionFailure(_logger, typeof(TDbContext).Name, exception);
        }
    }

    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        if (Volatile.Read(ref _hasSnapshot) == 1)
        {
            yield return new Measurement<long>(Interlocked.Read(ref _backlog), _tags);
        }
    }

    private IEnumerable<Measurement<double>> ObserveOldestAge()
    {
        if (Volatile.Read(ref _hasSnapshot) == 1)
        {
            yield return new Measurement<double>(Volatile.Read(ref _oldestAgeSeconds), _tags);
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Unable to collect messaging outbox metrics for {DbContext}; retaining the last successful sample")]
    private static partial void LogCollectionFailure(
        ILogger logger,
        string dbContext,
        Exception exception);
}
