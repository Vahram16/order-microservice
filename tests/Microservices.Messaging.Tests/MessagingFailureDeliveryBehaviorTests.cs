using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microservices.Contracts;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Microservices.Messaging.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MessagingBehaviorCollection : ICollectionFixture<MessagingBehaviorFixture>
{
    public const string Name = "messaging-behavior";
}

[Collection(MessagingBehaviorCollection.Name)]
public sealed class MessagingFailureDeliveryBehaviorTests(MessagingBehaviorFixture fixture)
{
    [Fact]
    public async Task TransientFailureSucceedsAfterConfiguredRetry()
    {
        var message = TestMessageFactory.RetrySuccess();

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        Assert.Equal(3, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task PermanentFailureReceivesOnlyOneAttempt()
    {
        var message = TestMessageFactory.PermanentFailure();

        await fixture.PublishAsync(message);
        await fixture.WaitForErrorQueueAsync<PermanentFailureConsumer>();

        Assert.Equal(1, fixture.Probe.AttemptCount(message.MessageId));
    }

    [Fact]
    public async Task RetryExhaustionRoutesMessageToErrorQueue()
    {
        var message = TestMessageFactory.RetryExhaustion();

        await fixture.PublishAsync(message);
        await fixture.WaitForErrorQueueAsync<RetryExhaustionConsumer>();

        Assert.Equal(6, fixture.Probe.AttemptCount(message.MessageId));
    }

    [Fact]
    public async Task DelayedRedeliveryFollowsConfiguredIncreasingIntervals()
    {
        var message = TestMessageFactory.Redelivery();

        await fixture.PublishAsync(message);
        await fixture.WaitForErrorQueueAsync<RedeliveryConsumer>();

        var attempts = fixture.Probe.Attempts(message.MessageId);
        Assert.Equal(6, attempts.Count);

        var firstRedeliveryDelay = attempts[2] - attempts[1];
        var secondRedeliveryDelay = attempts[4] - attempts[3];
        Assert.True(firstRedeliveryDelay >= TimeSpan.FromMilliseconds(200));
        Assert.True(secondRedeliveryDelay >= TimeSpan.FromMilliseconds(450));
        Assert.True(secondRedeliveryDelay > firstRedeliveryDelay);
    }

    [Fact]
    public async Task DuplicateDeliveryDoesNotRepeatDatabaseChanges()
    {
        var message = TestMessageFactory.Duplicate();

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);
        await fixture.PublishAsync(message);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(1, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task TransactionRollbackPublishesNoMessages()
    {
        var message = TestMessageFactory.OutboxProduced();
        var effectId = Guid.NewGuid();

        await fixture.ExecuteBusOutboxTransactionAsync(effectId, message, commit: false);
        await Task.Delay(TimeSpan.FromMilliseconds(750));

        Assert.Equal(0, fixture.Probe.CompletionCount(message.MessageId));
        Assert.Equal(0, await fixture.GetEffectCountAsync(effectId));
        Assert.Equal(0, await fixture.GetOutboxBacklogAsync());
    }

    [Fact]
    public async Task SuccessfulCommitPublishesExactlyOnceThroughOutbox()
    {
        var message = TestMessageFactory.OutboxProduced();
        var effectId = Guid.NewGuid();

        await fixture.ExecuteBusOutboxTransactionAsync(effectId, message, commit: true);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);
        await fixture.WaitForOutboxToDrainAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, fixture.Probe.CompletionCount(message.MessageId));
        Assert.Equal(1, await fixture.GetEffectCountAsync(effectId));
        Assert.Equal(0, await fixture.GetOutboxBacklogAsync());
    }

    [Fact]
    public async Task BrokerConnectionRecoversAfterForcedDisconnect()
    {
        await fixture.RabbitMq.DisconnectAllClientsAsync();
        await fixture.RabbitMq.WaitForConnectionsAsync(1);

        var message = TestMessageFactory.RetrySuccess();
        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task DatabaseConnectionRecoversAfterBackendTermination()
    {
        var message = TestMessageFactory.DatabaseRecovery();

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        Assert.True(fixture.Probe.AttemptCount(message.MessageId) >= 2);
        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public Task GracefulShutdownDrainsActiveConsumer() =>
        fixture.VerifyGracefulDrainAsync();
}

public sealed class MessagingBehaviorFixture : IAsyncLifetime
{
    private const string PostgreSqlEnvironmentVariable =
        "MESSAGING_TEST_POSTGRES_CONNECTION_STRING";
    private const string RabbitMqEnvironmentVariable =
        "MESSAGING_TEST_RABBITMQ_CONNECTION_STRING";
    private IHost? _host;
    private string _prefix = string.Empty;

    public DeliveryProbe Probe { get; } = new();

    public RabbitMqManagementClient RabbitMq { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var postgres = RequiredEnvironmentVariable(PostgreSqlEnvironmentVariable);
        var rabbitMq = RequiredEnvironmentVariable(RabbitMqEnvironmentVariable);
        _prefix = $"messaging-policy-{Guid.NewGuid():N}"[..34];
        RabbitMq = RabbitMqManagementClient.CreateFromEnvironment();

        _host = BuildHost(postgres, rabbitMq, _prefix, Probe, registrations =>
        {
            registrations.AddConsumer<RetrySuccessConsumer>();
            registrations.AddConsumer<PermanentFailureConsumer>();
            registrations.AddConsumer<RetryExhaustionConsumer>();
            registrations.AddConsumer<RedeliveryConsumer>();
            registrations.AddConsumer<DuplicateDeliveryConsumer>();
            registrations.AddConsumer<OutboxProducedConsumer>();
            registrations.AddConsumer<DatabaseRecoveryConsumer>();
        });

        await RecreateDatabaseAsync(_host.Services);
        await _host.StartAsync();
        await RabbitMq.WaitForConnectionsAsync(1);
    }

    public async Task DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
        _host.Dispose();
    }

    public async Task PublishAsync<T>(T message)
        where T : class
    {
        var bus = Services.GetRequiredService<IBus>();
        await bus.Publish(message);
    }

    public async Task ExecuteBusOutboxTransactionAsync(
        Guid effectId,
        OutboxProduced message,
        bool commit)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MessagingBehaviorDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            dbContext.Effects.Add(new MessagingEffect(effectId, 1));
            await publishEndpoint.Publish(message);
            await dbContext.SaveChangesAsync();

            if (commit)
            {
                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }
        });
    }

    public async Task<int> GetEffectCountAsync(Guid id)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MessagingBehaviorDbContext>();
        return await dbContext.Effects
            .Where(effect => effect.Id == id)
            .Select(effect => effect.Count)
            .SingleOrDefaultAsync();
    }

    public async Task<long> GetOutboxBacklogAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MessagingBehaviorDbContext>();
        return await dbContext.Set<OutboxMessage>().LongCountAsync();
    }

    public async Task WaitForOutboxToDrainAsync()
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await GetOutboxBacklogAsync() == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("The PostgreSQL messaging outbox did not drain.");
    }

    public Task WaitForErrorQueueAsync<TConsumer>()
        where TConsumer : class, IConsumer =>
        RabbitMq.WaitForQueueMessagesAsync(
            $"{EndpointName<TConsumer>()}_error",
            minimumMessages: 1);

    public async Task VerifyGracefulDrainAsync()
    {
        var postgres = RequiredEnvironmentVariable(PostgreSqlEnvironmentVariable);
        var rabbitMq = RequiredEnvironmentVariable(RabbitMqEnvironmentVariable);
        var drainPrefix = $"messaging-drain-{Guid.NewGuid():N}"[..33];
        var gate = new DrainGate();
        using var host = BuildHost(postgres, rabbitMq, drainPrefix, gate, registrations =>
            registrations.AddConsumer<DrainConsumer>());

        await host.StartAsync();
        var bus = host.Services.GetRequiredService<IBus>();
        var message = TestMessageFactory.Drain();
        await bus.Publish(message);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stopTask = host.StopAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.False(stopTask.IsCompleted);

        gate.Release.TrySetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(gate.Completed.Task.IsCompletedSuccessfully);
    }

    private IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("The test host is not started.");

    private string EndpointName<TConsumer>()
        where TConsumer : class, IConsumer =>
        new KebabCaseEndpointNameFormatter(_prefix, includeNamespace: false)
            .Consumer<TConsumer>();

    private static IHost BuildHost<TProbe>(
        string postgres,
        string rabbitMq,
        string prefix,
        TProbe probe,
        Action<IBusRegistrationConfigurator> configureConsumers)
        where TProbe : class
    {
        var formatter = new KebabCaseEndpointNameFormatter(prefix, includeNamespace: false);
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = rabbitMq,
            ["Messaging:UseTls"] = "false",
            ["Messaging:RetryIntervals:0"] = "00:00:00.025",
            ["Messaging:RedeliveryIntervals:0"] = "00:00:00.250",
            ["Messaging:RedeliveryIntervals:1"] = "00:00:00.500",
            ["Messaging:PrefetchCount"] = "1",
            ["Messaging:ConcurrentMessageLimit"] = "1",
            ["Messaging:StartTimeout"] = "00:00:15",
            ["Messaging:StopTimeout"] = "00:00:10",
            ["Messaging:ConsumerStopTimeout"] = "00:00:08",
            ["Messaging:OutboxQueryDelay"] = "00:00:00.050",
            ["Messaging:OutboxMetricsInterval"] = "00:00:00.250",
            ["Messaging:DuplicateDetectionWindow"] = "00:05:00",
            ["Messaging:QueueMessageTimeToLive"] = "00:30:00",
            ["Messaging:FaultQueueRetention"] = "00:30:00",
            ["Messaging:QueueMaxLength"] = "1000",
            ["Messaging:QueueMaxLengthBytes"] = "10485760",
            ["Messaging:MaximumMessageBytes"] = "1048576",
            ["Messaging:FaultQueueMaxLength"] = "1000"
        };

        SetConsumerIntervals<RetrySuccessConsumer>(
            values,
            formatter,
            [TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50)],
            [TimeSpan.FromSeconds(1)]);
        SetConsumerIntervals<PermanentFailureConsumer>(
            values,
            formatter,
            [TimeSpan.FromMilliseconds(25)],
            [TimeSpan.FromMilliseconds(250)]);
        SetConsumerIntervals<RetryExhaustionConsumer>(
            values,
            formatter,
            [TimeSpan.FromMilliseconds(25)],
            [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)]);
        SetConsumerIntervals<RedeliveryConsumer>(
            values,
            formatter,
            [TimeSpan.FromMilliseconds(25)],
            [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)]);
        SetConsumerIntervals<DatabaseRecoveryConsumer>(
            values,
            formatter,
            [TimeSpan.FromMilliseconds(100)],
            [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)]);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddSingleton(probe);
        builder.Services.AddDbContext<MessagingBehaviorDbContext>(options =>
            options.UseNpgsql(postgres, npgsql => npgsql.EnableRetryOnFailure()));
        builder.Services.AddRabbitMqWithPostgresOutbox<MessagingBehaviorDbContext>(
            builder.Configuration,
            prefix,
            configureConsumers);

        return builder.Build();
    }

    private static void SetConsumerIntervals<TConsumer>(
        IDictionary<string, string?> values,
        IEndpointNameFormatter formatter,
        IReadOnlyList<TimeSpan> retryIntervals,
        IReadOnlyList<TimeSpan> redeliveryIntervals)
        where TConsumer : class, IConsumer
    {
        var endpoint = formatter.Consumer<TConsumer>();
        for (var index = 0; index < retryIntervals.Count; index++)
        {
            values[$"Messaging:Consumers:{endpoint}:RetryIntervals:{index}"] =
                retryIntervals[index].ToString("c");
        }

        for (var index = 0; index < redeliveryIntervals.Count; index++)
        {
            values[$"Messaging:Consumers:{endpoint}:RedeliveryIntervals:{index}"] =
                redeliveryIntervals[index].ToString("c");
        }
    }

    private static async Task RecreateDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MessagingBehaviorDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable '{name}' is required.");
}

public sealed class DeliveryProbe
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DateTimeOffset>> _attempts = new();
    private readonly ConcurrentDictionary<Guid, int> _completions = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _completionSignals = new();

    public int RecordAttempt(Guid messageId)
    {
        var attempts = _attempts.GetOrAdd(messageId, static _ => new ConcurrentQueue<DateTimeOffset>());
        attempts.Enqueue(DateTimeOffset.UtcNow);
        return attempts.Count;
    }

    public void Complete(Guid messageId)
    {
        _completions.AddOrUpdate(messageId, 1, static (_, count) => count + 1);
        _completionSignals.GetOrAdd(
            messageId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();
    }

    public int AttemptCount(Guid messageId) =>
        _attempts.TryGetValue(messageId, out var attempts) ? attempts.Count : 0;

    public int CompletionCount(Guid messageId) =>
        _completions.GetValueOrDefault(messageId);

    public IReadOnlyList<DateTimeOffset> Attempts(Guid messageId) =>
        _attempts.TryGetValue(messageId, out var attempts) ? attempts.ToArray() : [];

    public Task WaitForCompletionAsync(Guid messageId) =>
        _completionSignals.GetOrAdd(
                messageId,
                static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .Task
            .WaitAsync(TimeSpan.FromSeconds(15));
}

public sealed class RabbitMqManagementClient(HttpClient client)
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

    public async Task WaitForQueueMessagesAsync(string queueName, int minimumMessages)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            using var response = await client.GetAsync(QueuePath(queueName));
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                if (document.RootElement.GetProperty("messages").GetInt32() >= minimumMessages)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"RabbitMQ queue '{queueName}' did not reach the expected depth.");
    }

    public async Task DisconnectAllClientsAsync()
    {
        using var response = await client.GetAsync("/api/connections");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        foreach (var connection in document.RootElement.EnumerateArray())
        {
            var name = connection.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            using var delete = await client.DeleteAsync($"/api/connections/{Uri.EscapeDataString(name)}");
            delete.EnsureSuccessStatusCode();
        }
    }

    public async Task WaitForConnectionsAsync(int minimumConnections)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            using var response = await client.GetAsync("/api/connections");
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                if (document.RootElement.GetArrayLength() >= minimumConnections)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("RabbitMQ clients did not reconnect within the expected time.");
    }

    private static string QueuePath(string queueName) =>
        $"/api/queues/%2F/{Uri.EscapeDataString(queueName)}";

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable '{name}' is required.");
}

public sealed class MessagingBehaviorDbContext(DbContextOptions<MessagingBehaviorDbContext> options)
    : DbContext(options)
{
    public DbSet<MessagingEffect> Effects => Set<MessagingEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MessagingEffect>(entity =>
        {
            entity.HasKey(effect => effect.Id);
            entity.Property(effect => effect.Count);
        });
        modelBuilder.AddMassTransitOutboxEntities();
    }
}

public sealed class MessagingEffect(Guid id, int count)
{
    public Guid Id { get; private set; } = id;

    public int Count { get; private set; } = count;
}

public sealed class RetrySuccessConsumer(
    DeliveryProbe probe,
    MessagingBehaviorDbContext dbContext) : IConsumer<RetrySuccess>
{
    public async Task Consume(ConsumeContext<RetrySuccess> context)
    {
        if (probe.RecordAttempt(context.Message.MessageId) < 3)
        {
            throw new TestTransientFailure();
        }

        dbContext.Effects.Add(new MessagingEffect(context.Message.MessageId, 1));
        await dbContext.SaveChangesAsync(context.CancellationToken);
        probe.Complete(context.Message.MessageId);
    }
}

public sealed class PermanentFailureConsumer(DeliveryProbe probe) : IConsumer<PermanentFailure>
{
    public Task Consume(ConsumeContext<PermanentFailure> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        throw new TestPermanentFailure();
    }
}

public sealed class RetryExhaustionConsumer(DeliveryProbe probe) : IConsumer<RetryExhaustion>
{
    public Task Consume(ConsumeContext<RetryExhaustion> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        throw new TestTransientFailure();
    }
}

public sealed class RedeliveryConsumer(DeliveryProbe probe) : IConsumer<RedeliveryFailure>
{
    public Task Consume(ConsumeContext<RedeliveryFailure> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        throw new TestTransientFailure();
    }
}

public sealed class DuplicateDeliveryConsumer(
    DeliveryProbe probe,
    MessagingBehaviorDbContext dbContext) : IConsumer<DuplicateDelivery>
{
    public async Task Consume(ConsumeContext<DuplicateDelivery> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        dbContext.Effects.Add(new MessagingEffect(context.Message.MessageId, 1));
        await dbContext.SaveChangesAsync(context.CancellationToken);
        probe.Complete(context.Message.MessageId);
    }
}

public sealed class OutboxProducedConsumer(DeliveryProbe probe) : IConsumer<OutboxProduced>
{
    public Task Consume(ConsumeContext<OutboxProduced> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class DatabaseRecoveryConsumer(
    DeliveryProbe probe,
    MessagingBehaviorDbContext dbContext) : IConsumer<DatabaseRecovery>
{
    public async Task Consume(ConsumeContext<DatabaseRecovery> context)
    {
        var attempt = probe.RecordAttempt(context.Message.MessageId);
        if (attempt == 1)
        {
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT pg_terminate_backend(pg_backend_pid())",
                    context.CancellationToken);
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT 1",
                    context.CancellationToken);
            }
            catch (NpgsqlException)
            {
                throw;
            }

            throw new TestTransientFailure();
        }

        dbContext.Effects.Add(new MessagingEffect(context.Message.MessageId, 1));
        await dbContext.SaveChangesAsync(context.CancellationToken);
        probe.Complete(context.Message.MessageId);
    }
}

public sealed class DrainConsumer(DrainGate gate) : IConsumer<DrainMessage>
{
    public async Task Consume(ConsumeContext<DrainMessage> context)
    {
        gate.Entered.TrySetResult();
        await gate.Release.Task.WaitAsync(context.CancellationToken);
        gate.Completed.TrySetResult();
    }
}

public sealed class DrainGate
{
    public TaskCompletionSource Entered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Completed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class TestTransientFailure : Exception, ITransientConsumerFailure;

public sealed class TestPermanentFailure : Exception, IPermanentConsumerFailure;

public sealed record RetrySuccess(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record PermanentFailure(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record RetryExhaustion(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record RedeliveryFailure(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record DuplicateDelivery(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record OutboxProduced(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record DatabaseRecovery(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

public sealed record DrainMessage(
    Guid MessageId,
    Guid CorrelationId,
    Guid? CausationId,
    int ContractVersion) : IIntegrationMessage;

internal static class TestMessageFactory
{
    public static RetrySuccess RetrySuccess() => Create(
        static (messageId, correlationId) => new RetrySuccess(messageId, correlationId, null, 1));

    public static PermanentFailure PermanentFailure() => Create(
        static (messageId, correlationId) => new PermanentFailure(messageId, correlationId, null, 1));

    public static RetryExhaustion RetryExhaustion() => Create(
        static (messageId, correlationId) => new RetryExhaustion(messageId, correlationId, null, 1));

    public static RedeliveryFailure Redelivery() => Create(
        static (messageId, correlationId) => new RedeliveryFailure(messageId, correlationId, null, 1));

    public static DuplicateDelivery Duplicate() => Create(
        static (messageId, correlationId) => new DuplicateDelivery(messageId, correlationId, null, 1));

    public static OutboxProduced OutboxProduced() => Create(
        static (messageId, correlationId) => new OutboxProduced(messageId, correlationId, null, 1));

    public static DatabaseRecovery DatabaseRecovery() => Create(
        static (messageId, correlationId) => new DatabaseRecovery(messageId, correlationId, null, 1));

    public static DrainMessage Drain() => Create(
        static (messageId, correlationId) => new DrainMessage(messageId, correlationId, null, 1));

    private static T Create<T>(Func<Guid, Guid, T> factory)
    {
        var messageId = NewId.NextGuid();
        return factory(messageId, NewId.NextGuid());
    }
}
