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
        description: "Immediate retry invocations. Retries inside a broker redelivery are included.");

    public static readonly Counter<long> RedeliveryDeliveries = Meter.CreateCounter<long>(
        "messaging.consumer.redelivery.deliveries",
        description: "Broker-backed redelivery lifecycles, counted once before their immediate retry sequence.");

    public static readonly Counter<long> ConsumerAttemptFailures = Meter.CreateCounter<long>(
        "messaging.consumer.attempt.failures",
        description: "Individual consumer invocations that threw, including transient attempts later recovered.");

    public static readonly Histogram<double> ConsumerAttemptDuration = Meter.CreateHistogram<double>(
        "messaging.consumer.attempt.duration",
        unit: "s",
        description: "Duration of one consumer invocation, not the complete retry/redelivery lifecycle.");

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
        var endpoint = GetStableEndpointName(context.ReceiveContext.InputAddress);
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
            MessagingInstrumentation.RedeliveryDeliveries.Add(1, tags);
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
            MessagingInstrumentation.ConsumerAttemptFailures.Add(1, tags);
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
        context.CreateFilterScope("consumerAttemptMetrics");

    private static string GetStableEndpointName(Uri inputAddress)
    {
        var endpoint = inputAddress.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        return endpoint is not null && RabbitMqMessagingOptionsValidator.IsValidEndpointName(endpoint)
            ? endpoint
            : "unknown";
    }
}

internal sealed partial class OutboxMetricsCollector<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxMetricsCollector<TDbContext>> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _queryTimeout;
    private readonly KeyValuePair<string, object?>[] _collectorTags;
    private readonly ObservableGauge<long> _backlogGauge;
    private readonly ObservableGauge<double> _oldestAgeGauge;
    private readonly ObservableGauge<int> _healthGauge;
    private readonly ObservableGauge<double> _lastSuccessAgeGauge;
    private OutboxMetricSnapshot[] _snapshots = [];
    private long _lastSuccessUtcTicks;
    private long _nextFailureLogUtcTicks;
    private int _healthy;

    public OutboxMetricsCollector(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxMetricsCollector<TDbContext>> logger,
        RabbitMqMessagingOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = options.OutboxMetricsInterval;
        _queryTimeout = options.OutboxMetricsQueryTimeout;
        _collectorTags = [new KeyValuePair<string, object?>("db.context", typeof(TDbContext).Name)];
        _backlogGauge = MessagingInstrumentation.Meter.CreateObservableGauge(
            "messaging.outbox.backlog",
            ObserveBacklog,
            description: "Pending OutboxMessage rows, split by bounded outbox role.");
        _oldestAgeGauge = MessagingInstrumentation.Meter.CreateObservableGauge(
            "messaging.outbox.oldest.age",
            ObserveOldestAge,
            unit: "s",
            description: "Age of the oldest pending OutboxMessage row, split by bounded outbox role.");
        _healthGauge = MessagingInstrumentation.Meter.CreateObservableGauge(
            "messaging.outbox.collector.healthy",
            ObserveHealth,
            description: "One after a successful collection, zero before first success or after collection failure.");
        _lastSuccessAgeGauge = MessagingInstrumentation.Meter.CreateObservableGauge(
            "messaging.outbox.collector.last_success.age",
            ObserveLastSuccessAge,
            unit: "s",
            description: "Seconds since the outbox collector last completed successfully.");
    }

    internal IReadOnlyList<OutboxMetricSnapshot> CurrentSnapshots =>
        Volatile.Read(ref _snapshots);

    internal bool IsHealthy => Volatile.Read(ref _healthy) == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CollectAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await CollectAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task CollectAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queryCancellation.CancelAfter(_queryTimeout);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
            var outboxMessages = dbContext.Set<OutboxMessage>().AsNoTracking();
            var bus = await QueryRoleAsync(
                    outboxMessages.Where(message => message.OutboxId != null),
                    OutboxRole.Bus,
                    queryCancellation.Token)
                .ConfigureAwait(false);
            var consumer = await QueryRoleAsync(
                    outboxMessages.Where(message =>
                        message.OutboxId == null &&
                        message.InboxMessageId != null &&
                        message.InboxConsumerId != null),
                    OutboxRole.Consumer,
                    queryCancellation.Token)
                .ConfigureAwait(false);

            Volatile.Write(ref _snapshots, [bus, consumer]);
            Interlocked.Exchange(ref _lastSuccessUtcTicks, DateTime.UtcNow.Ticks);
            Volatile.Write(ref _healthy, 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Last-known backlog values deliberately remain unchanged. Health and staleness expose
            // the failure independently so an unavailable database can never look like zero work.
            Volatile.Write(ref _healthy, 0);
            MessagingInstrumentation.OutboxCollectionFailures.Add(1, _collectorTags);
            LogCollectionFailureWithRateLimit(exception);
        }
    }

    private static async Task<OutboxMetricSnapshot> QueryRoleAsync(
        IQueryable<OutboxMessage> query,
        OutboxRole role,
        CancellationToken cancellationToken)
    {
        var aggregate = await query
            .GroupBy(static _ => 1)
            .Select(group => new OutboxAggregate(
                group.LongCount(),
                group.Min(message => message.SentTime)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return aggregate is null
            ? new OutboxMetricSnapshot(role, 0, 0)
            : new OutboxMetricSnapshot(
                role,
                aggregate.Count,
                Math.Max(0, (DateTime.UtcNow - aggregate.OldestSentTime).TotalSeconds));
    }

    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        foreach (var snapshot in Volatile.Read(ref _snapshots))
        {
            yield return new Measurement<long>(snapshot.Count, CreateRoleTags(snapshot.Role));
        }
    }

    private IEnumerable<Measurement<double>> ObserveOldestAge()
    {
        foreach (var snapshot in Volatile.Read(ref _snapshots))
        {
            yield return new Measurement<double>(snapshot.OldestAgeSeconds, CreateRoleTags(snapshot.Role));
        }
    }

    private Measurement<int> ObserveHealth() =>
        new(Volatile.Read(ref _healthy), _collectorTags);

    private Measurement<double> ObserveLastSuccessAge()
    {
        var ticks = Interlocked.Read(ref _lastSuccessUtcTicks);
        var age = ticks == 0
            ? double.NaN
            : Math.Max(0, (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds);
        return new Measurement<double>(age, _collectorTags);
    }

    private KeyValuePair<string, object?>[] CreateRoleTags(OutboxRole role) =>
    [
        _collectorTags[0],
        new KeyValuePair<string, object?>("outbox.role", role.ToString().ToLowerInvariant())
    ];

    private void LogCollectionFailureWithRateLimit(Exception exception)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var nextLogTicks = Interlocked.Read(ref _nextFailureLogUtcTicks);
        if (nextLogTicks > nowTicks)
        {
            return;
        }

        if (Interlocked.CompareExchange(
                ref _nextFailureLogUtcTicks,
                nowTicks + FailureLogInterval.Ticks,
                nextLogTicks) == nextLogTicks)
        {
            LogCollectionFailure(_logger, typeof(TDbContext).Name, exception);
        }
    }

    internal enum OutboxRole
    {
        Bus = 0,
        Consumer = 1
    }

    internal sealed record OutboxMetricSnapshot(
        OutboxRole Role,
        long Count,
        double OldestAgeSeconds);

    private sealed record OutboxAggregate(
        long Count,
        DateTime OldestSentTime);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Unable to collect messaging outbox metrics for {DbContext}; last known backlog values remain stale")]
    private static partial void LogCollectionFailure(
        ILogger logger,
        string dbContext,
        Exception exception);
}
