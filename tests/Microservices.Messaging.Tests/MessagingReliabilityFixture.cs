using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microservices.Application.Messaging;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microservices.Messaging.Tests;

public sealed class MessagingReliabilityFixture : IAsyncLifetime
{
    private const string PostgreSqlEnvironmentVariable =
        "MESSAGING_TEST_POSTGRES_CONNECTION_STRING";
    private const string RabbitMqEnvironmentVariable =
        "MESSAGING_TEST_RABBITMQ_CONNECTION_STRING";

    private readonly Dictionary<Type, string> _endpoints = [];
    private IHost? _host;
    private string _postgres = string.Empty;
    private string _rabbitMq = string.Empty;
    private string _prefix = string.Empty;

    public DeliveryProbe Probe { get; } = new();

    public MessagingMetricRecorder Metrics { get; } = new();

    public RabbitMqManagementClient RabbitMq { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _postgres = RequiredEnvironmentVariable(PostgreSqlEnvironmentVariable);
        _rabbitMq = RequiredEnvironmentVariable(RabbitMqEnvironmentVariable);
        _prefix = $"messaging-reliability-{Guid.NewGuid():N}"[..48];
        RegisterEndpoint<SuccessConsumer>("success");
        RegisterEndpoint<OneRetryConsumer>("one-retry");
        RegisterEndpoint<MultipleRetryConsumer>("multiple-retry");
        RegisterEndpoint<RedeliverySuccessConsumer>("redelivery-success");
        RegisterEndpoint<ExhaustedConsumer>("exhausted");
        RegisterEndpoint<PermanentConsumer>("permanent");
        RegisterEndpoint<DuplicateConsumer>("duplicate");
        RegisterEndpoint<OutboxProducedConsumer>("outbox-produced");
        RegisterEndpoint<ParentConsumer>("parent");
        RegisterEndpoint<ChildConsumer>("child");

        RabbitMq = RabbitMqManagementClient.CreateFromEnvironment();
        _host = BuildHost(_postgres, _rabbitMq, _prefix, Probe, _endpoints);
        await RecreateDatabaseAsync(_host.Services).ConfigureAwait(false);
        await _host.StartAsync().ConfigureAwait(false);
        await RabbitMq.WaitForConnectionsAsync(1).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }

        Metrics.Dispose();
        RabbitMq.Dispose();
    }

    public string Endpoint<TConsumer>()
        where TConsumer : class, IConsumer =>
        _endpoints[typeof(TConsumer)];

    public async Task PublishAsync<TMessage>(
        TMessage message,
        Guid? messageId = null,
        Guid? correlationId = null)
        where TMessage : class
    {
        var bus = Services.GetRequiredService<IBus>();
        await bus.Publish(
            message,
            context =>
            {
                if (messageId is not null)
                {
                    context.MessageId = messageId;
                }

                if (correlationId is not null)
                {
                    context.CorrelationId = correlationId;
                }
            }).ConfigureAwait(false);
    }

    public async Task SendToEndpointAsync<TMessage>(string endpointName, TMessage message)
        where TMessage : class
    {
        var bus = Services.GetRequiredService<IBus>();
        var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{endpointName}"))
            .ConfigureAwait(false);
        await endpoint.Send(message).ConfigureAwait(false);
    }

    public async Task ExecuteBusOutboxTransactionAsync(
        Guid effectId,
        OutboxProducedMessage message,
        bool commit)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationMessagePublisher>();
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync()
                .ConfigureAwait(false);
            dbContext.Effects.Add(new ReliabilityEffect(effectId, 1));
            await publisher.PublishAsync(message).ConfigureAwait(false);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            if (commit)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async Task<int> GetEffectCountAsync(Guid id)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        return await dbContext.Effects
            .Where(effect => effect.Id == id)
            .Select(effect => effect.Count)
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task WaitForStableEffectCountAsync(Guid id, int expectedCount)
    {
        var deadline = Stopwatch.StartNew();
        var stableSince = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await GetEffectCountAsync(id).ConfigureAwait(false) != expectedCount)
            {
                stableSince.Restart();
            }
            else if (stableSince.Elapsed >= TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Effect '{id}' did not remain at count {expectedCount}.");
    }

    public async Task WaitForOutboxToDrainAsync()
    {
        await WaitUntilAsync(
            async () =>
            {
                await using var scope = Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
                return await dbContext.Set<OutboxMessage>()
                    .AsNoTracking()
                    .AnyAsync()
                    .ConfigureAwait(false) is false;
            },
            "PostgreSQL outbox did not drain.").ConfigureAwait(false);
    }

    public async Task AssertNoCompletionAsync(Guid messageId)
    {
        var completed = await Probe.CompletionTask(messageId)
            .WaitAsync(TimeSpan.FromMilliseconds(750))
            .ContinueWith(
                static _ => true,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default)
            .WaitAsync(TimeSpan.FromSeconds(1))
            .ConfigureAwait(false);

        Assert.False(completed);
    }

    public async Task VerifyGracefulDrainAsync()
    {
        var drainPrefix = $"messaging-drain-{Guid.NewGuid():N}"[..42];
        var endpoint = $"{drainPrefix}-active";
        var gate = new DrainGate();
        var endpoints = new Dictionary<Type, string>
        {
            [typeof(DrainConsumer)] = endpoint
        };

        using var host = BuildHost(_postgres, _rabbitMq, drainPrefix, gate, endpoints);
        await host.StartAsync().ConfigureAwait(false);
        var bus = host.Services.GetRequiredService<IBus>();
        await bus.Publish(new DrainMessage(Guid.NewGuid())).ConfigureAwait(false);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        var stopTask = host.StopAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        Assert.False(stopTask.IsCompleted);

        gate.Release.TrySetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.True(gate.Completed.Task.IsCompletedSuccessfully);
    }

    private IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("The test host is not started.");

    private void RegisterEndpoint<TConsumer>(string suffix)
        where TConsumer : class, IConsumer =>
        _endpoints.Add(typeof(TConsumer), $"{_prefix}-{suffix}");

    private static IHost BuildHost<TProbe>(
        string postgres,
        string rabbitMq,
        string prefix,
        TProbe probe,
        IReadOnlyDictionary<Type, string> endpoints)
        where TProbe : class
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = rabbitMq,
            ["Messaging:UseTls"] = "false",
            ["Messaging:RetryIntervals:0"] = "00:00:00.020",
            ["Messaging:RedeliveryIntervals:0"] = "00:00:00.200",
            ["Messaging:PrefetchCount"] = "1",
            ["Messaging:ConcurrentMessageLimit"] = "1",
            ["Messaging:StartTimeout"] = "00:00:15",
            ["Messaging:StopTimeout"] = "00:00:10",
            ["Messaging:ConsumerStopTimeout"] = "00:00:08",
            ["Messaging:OutboxQueryDelay"] = "00:00:00.050",
            ["Messaging:OutboxMetricsInterval"] = "00:00:00.250",
            ["Messaging:OutboxMetricsQueryTimeout"] = "00:00:02",
            ["Messaging:DuplicateDetectionWindow"] = "00:05:00",
            ["Messaging:FaultQueueRetention"] = "00:30:00",
            ["Messaging:QueueMaxLength"] = "1000",
            ["Messaging:QueueMaxLengthBytes"] = "10485760",
            ["Messaging:MaximumMessageBytes"] = "1048576",
            ["Messaging:FaultQueueMaxLength"] = "1000",
            ["Messaging:MaximumRetryAndRedeliveryDelay"] = "00:00:10",
            ["Messaging:AllowValidatedDefaultConsumerPolicy"] = "false"
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddSingleton(probe);
        builder.Services.AddDbContext<ReliabilityDbContext>(options =>
            options.UseNpgsql(postgres, npgsql => npgsql.EnableRetryOnFailure()));
        builder.Services.AddRabbitMqWithPostgresOutbox<ReliabilityDbContext>(
            builder.Configuration,
            prefix,
            registrations => RegisterConsumers(registrations, endpoints));

        return builder.Build();
    }

    private static void RegisterConsumers(
        IBusRegistrationConfigurator registrations,
        IReadOnlyDictionary<Type, string> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            RegisterConsumer(registrations, endpoint.Key, endpoint.Value);
        }
    }

    private static void RegisterConsumer(
        IBusRegistrationConfigurator registrations,
        Type consumerType,
        string endpointName)
    {
        var policy = PolicyFor(consumerType);

        if (consumerType == typeof(SuccessConsumer))
            registrations.AddConsumerWithPolicy<SuccessConsumer>(endpointName, policy);
        else if (consumerType == typeof(OneRetryConsumer))
            registrations.AddConsumerWithPolicy<OneRetryConsumer>(endpointName, policy);
        else if (consumerType == typeof(MultipleRetryConsumer))
            registrations.AddConsumerWithPolicy<MultipleRetryConsumer>(endpointName, policy);
        else if (consumerType == typeof(RedeliverySuccessConsumer))
            registrations.AddConsumerWithPolicy<RedeliverySuccessConsumer>(endpointName, policy);
        else if (consumerType == typeof(ExhaustedConsumer))
            registrations.AddConsumerWithPolicy<ExhaustedConsumer>(endpointName, policy);
        else if (consumerType == typeof(PermanentConsumer))
            registrations.AddConsumerWithPolicy<PermanentConsumer>(endpointName, policy);
        else if (consumerType == typeof(DuplicateConsumer))
            registrations.AddConsumerWithPolicy<DuplicateConsumer>(endpointName, policy);
        else if (consumerType == typeof(OutboxProducedConsumer))
            registrations.AddConsumerWithPolicy<OutboxProducedConsumer>(endpointName, policy);
        else if (consumerType == typeof(ParentConsumer))
            registrations.AddConsumerWithPolicy<ParentConsumer>(endpointName, policy);
        else if (consumerType == typeof(ChildConsumer))
            registrations.AddConsumerWithPolicy<ChildConsumer>(endpointName, policy);
        else if (consumerType == typeof(DrainConsumer))
            registrations.AddConsumerWithPolicy<DrainConsumer>(endpointName, policy);
        else
            throw new InvalidOperationException($"No reliability policy exists for '{consumerType.FullName}'.");
    }

    private static ConsumerDeliveryPolicyOptions PolicyFor(Type consumerType)
    {
        var retries = consumerType == typeof(MultipleRetryConsumer)
            ? new[] { TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40) }
            : new[] { TimeSpan.FromMilliseconds(20) };
        var redeliveries = consumerType == typeof(ExhaustedConsumer)
            ? new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400) }
            : new[] { TimeSpan.FromMilliseconds(200) };

        return new ConsumerDeliveryPolicyOptions
        {
            RetryIntervals = retries,
            RedeliveryIntervals = redeliveries,
            PrefetchCount = 1,
            ConcurrentMessageLimit = 1,
            IsCritical = true
        };
    }

    private static async Task RecreateDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        await dbContext.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await dbContext.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string failureMessage)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        throw new TimeoutException(failureMessage);
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable '{name}' is required.");
}

public sealed class MessagingMetricRecorder : IDisposable
{
    private readonly ConcurrentDictionary<(string Instrument, string Endpoint), long> _values = [];
    private readonly MeterListener _listener = new();

    public MessagingMetricRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MessagingInstrumentation.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(RecordLong);
        _listener.SetMeasurementEventCallback<double>(RecordDouble);
        _listener.Start();
    }

    public MessagingMetricSnapshot Snapshot(string endpoint) =>
        new(
            Value("messaging.consumer.retry.attempts", endpoint),
            Value("messaging.consumer.redelivery.deliveries", endpoint),
            Value("messaging.consumer.attempt.failures", endpoint),
            Value("messaging.consumer.attempt.duration", endpoint));

    public MessagingMetricSnapshot Delta(
        MessagingMetricSnapshot baseline,
        string endpoint) =>
        Snapshot(endpoint) - baseline;

    public void Dispose() => _listener.Dispose();

    private void RecordLong(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var endpoint = Endpoint(tags);
        if (endpoint is not null)
        {
            _values.AddOrUpdate(
                (instrument.Name, endpoint),
                measurement,
                (_, current) => current + measurement);
        }
    }

    private void RecordDouble(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var endpoint = Endpoint(tags);
        if (endpoint is not null)
        {
            _values.AddOrUpdate(
                (instrument.Name, endpoint),
                1,
                static (_, current) => current + 1);
        }
    }

    private long Value(string instrument, string endpoint) =>
        _values.GetValueOrDefault((instrument, endpoint));

    private static string? Endpoint(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "messaging.destination.name")
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }
}

public readonly record struct MessagingMetricSnapshot(
    long ImmediateRetries,
    long RedeliveryDeliveries,
    long AttemptFailures,
    long AttemptDurations)
{
    public static MessagingMetricSnapshot operator -(
        MessagingMetricSnapshot left,
        MessagingMetricSnapshot right) =>
        new(
            left.ImmediateRetries - right.ImmediateRetries,
            left.RedeliveryDeliveries - right.RedeliveryDeliveries,
            left.AttemptFailures - right.AttemptFailures,
            left.AttemptDurations - right.AttemptDurations);
}

public sealed class RabbitMqManagementClient(HttpClient client) : IDisposable
{
    public static RabbitMqManagementClient CreateFromEnvironment()
    {
        var api = Required("MESSAGING_TEST_RABBITMQ_API");
        var username = Required("MESSAGING_TEST_RABBITMQ_USERNAME");
        var password = Required("MESSAGING_TEST_RABBITMQ_PASSWORD");
        var client = new HttpClient { BaseAddress = new Uri(api, UriKind.Absolute) };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return new RabbitMqManagementClient(client);
    }

    public async Task<int> QueueDepthAsync(string queueName)
    {
        using var response = await client.GetAsync(QueuePath(queueName)).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return 0;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var queue = document.RootElement;
        return ReadInt32(queue, "messages") ??
            (ReadInt32(queue, "messages_ready") ?? 0) +
            (ReadInt32(queue, "messages_unacknowledged") ?? 0);
    }

    public async Task WaitForQueueDepthAsync(string queueName, int expectedMinimum)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (await QueueDepthAsync(queueName).ConfigureAwait(false) >= expectedMinimum)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"RabbitMQ queue '{queueName}' did not reach depth {expectedMinimum}.");
    }

    public async Task<IReadOnlyDictionary<string, object?>> QueueArgumentsAsync(string queueName)
    {
        using var response = await client.GetAsync(QueuePath(queueName)).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.GetProperty("arguments").EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt64(out var value) => value,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }

        return result;
    }

    public async Task WaitForConnectionsAsync(int minimumConnections)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            using var response = await client.GetAsync("/api/connections").ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                if (document.RootElement.GetArrayLength() >= minimumConnections)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        throw new TimeoutException("RabbitMQ clients did not connect within the expected time.");
    }

    public void Dispose() => client.Dispose();

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string QueuePath(string queueName) =>
        $"/api/queues/%2F/{Uri.EscapeDataString(queueName)}" +
        "?disable_stats=false&enable_queue_totals=true";

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable '{name}' is required.");
}
