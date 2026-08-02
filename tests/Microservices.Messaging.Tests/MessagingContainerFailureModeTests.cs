using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microservices.Application.Messaging;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Microservices.Messaging.Tests;

[Collection(MessagingBehaviorTestGroup.Name)]
public sealed class MessagingContainerFailureModeTests
{
    [Fact]
    public async Task RabbitMqUnavailableDuringStartupFailsWithinConfiguredBound()
    {
        var unavailablePort = ReserveUnusedPort();
        using var host = BuildHost(
            RequiredEnvironmentVariable("MESSAGING_TEST_POSTGRES_CONNECTION_STRING"),
            $"amqp://guest:guest@127.0.0.1:{unavailablePort}/",
            $"unavailable-{Guid.NewGuid():N}"[..40],
            new DeliveryProbe(),
            startTimeout: TimeSpan.FromSeconds(1));

        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MissingDelayedExchangePluginFailsBusStartup()
    {
        await using var rabbit = new ContainerBuilder("rabbitmq:4.2.9-management")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "guest")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", "guest")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilMessageIsLogged(
                    "Server startup complete",
                    wait => wait.WithTimeout(TimeSpan.FromMinutes(1))))
            .Build();
        await rabbit.StartAsync();

        var connectionString = RabbitConnectionString(rabbit);
        using var host = BuildHost(
            RequiredEnvironmentVariable("MESSAGING_TEST_POSTGRES_CONNECTION_STRING"),
            connectionString,
            $"missing-plugin-{Guid.NewGuid():N}"[..46],
            new DeliveryProbe(),
            startTimeout: TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
        var text = exception.ToString();

        Assert.True(
            text.Contains("x-delayed-message", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("delayed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("COMMAND_INVALID", StringComparison.OrdinalIgnoreCase),
            text);
    }

    [Fact]
    public async Task CommittedOutboxRecoversAfterProcessStopsBeforeBrokerPublication()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18")
            .WithDatabase("reliability")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await using var rabbit = BuildProductionRabbitMqContainer();
        await postgres.StartAsync();
        await rabbit.StartAsync();

        var prefix = $"outbox-recovery-{Guid.NewGuid():N}"[..46];
        var message = ReliabilityMessageFactory.OutboxProduced();
        var firstProbe = new DeliveryProbe();
        using (var firstHost = BuildHost(
                   postgres.GetConnectionString(),
                   RabbitConnectionString(rabbit),
                   prefix,
                   firstProbe))
        {
            await RecreateDatabaseAsync(firstHost.Services);
            await firstHost.StartAsync();
            await rabbit.StopAsync();

            await using var scope = firstHost.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationMessagePublisher>();
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync();
                await publisher.PublishAsync(message);
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            Assert.True(await dbContext.Set<OutboxMessage>().AnyAsync());
            await StopIgnoringBrokerFailureAsync(firstHost);
        }

        await rabbit.StartAsync();
        var recoveryProbe = new DeliveryProbe();
        using var recoveryHost = BuildHost(
            postgres.GetConnectionString(),
            RabbitConnectionString(rabbit),
            prefix,
            recoveryProbe);
        await recoveryHost.StartAsync();

        await recoveryProbe.WaitForCompletionAsync(message.MessageId);
        await WaitForOutboxDrainAsync(recoveryHost.Services);
        Assert.Equal(1, recoveryProbe.CompletionCount(message.MessageId));
    }

    [Fact]
    public async Task CollectorDatabaseFailureRetainsLastKnownBacklogAndRecovers()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18")
            .WithDatabase("collector")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await using var rabbit = BuildProductionRabbitMqContainer();
        await postgres.StartAsync();
        await rabbit.StartAsync();

        var postgresConnectionString = postgres.GetConnectionString();
        var probe = new DeliveryProbe();
        using var host = BuildHost(
            postgresConnectionString,
            RabbitConnectionString(rabbit),
            $"collector-recovery-{Guid.NewGuid():N}"[..48],
            probe);
        await RecreateDatabaseAsync(host.Services);
        await host.StartAsync();

        await rabbit.StopAsync();
        var pendingMessage = ReliabilityMessageFactory.OutboxProduced();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationMessagePublisher>();
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync();
                await publisher.PublishAsync(pendingMessage);
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });
        }

        var collector = host.Services.GetRequiredService<OutboxMetricsCollector<ReliabilityDbContext>>();
        await collector.CollectAsync(CancellationToken.None);
        var known = Assert.Single(
            collector.CurrentSnapshots,
            snapshot => snapshot.Role ==
                OutboxMetricsCollector<ReliabilityDbContext>.OutboxRole.Bus);
        Assert.True(known.Count > 0);
        Assert.True(collector.IsHealthy);

        await SetDatabaseConnectionsAllowedAsync(postgresConnectionString, allowed: false);
        await collector.CollectAsync(CancellationToken.None);
        var stale = Assert.Single(
            collector.CurrentSnapshots,
            snapshot => snapshot.Role ==
                OutboxMetricsCollector<ReliabilityDbContext>.OutboxRole.Bus);
        Assert.Equal(known.Count, stale.Count);
        Assert.False(collector.IsHealthy);

        await SetDatabaseConnectionsAllowedAsync(postgresConnectionString, allowed: true);
        await WaitForCollectorRecoveryAsync(collector);
    }

    private static IContainer BuildProductionRabbitMqContainer() =>
        new ContainerBuilder("order-rabbitmq:ci")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "guest")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", "guest")
            .WithEnvironment("RABBITMQ_ERLANG_COOKIE", $"reliability-{Guid.NewGuid():N}")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilMessageIsLogged(
                    "Server startup complete",
                    wait => wait.WithTimeout(TimeSpan.FromMinutes(1))))
            .Build();

    private static IHost BuildHost(
        string postgres,
        string rabbitMq,
        string prefix,
        DeliveryProbe probe,
        TimeSpan? startTimeout = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = rabbitMq,
            ["Messaging:UseTls"] = "false",
            ["Messaging:RetryIntervals:0"] = "00:00:00.020",
            ["Messaging:RedeliveryIntervals:0"] = "00:00:00.100",
            ["Messaging:PrefetchCount"] = "1",
            ["Messaging:ConcurrentMessageLimit"] = "1",
            ["Messaging:StartTimeout"] = (startTimeout ?? TimeSpan.FromSeconds(10)).ToString("c"),
            ["Messaging:StopTimeout"] = "00:00:05",
            ["Messaging:ConsumerStopTimeout"] = "00:00:04",
            ["Messaging:OutboxQueryDelay"] = "00:00:00.050",
            ["Messaging:OutboxMetricsInterval"] = "00:00:00.250",
            ["Messaging:OutboxMetricsQueryTimeout"] = "00:00:01",
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
            registration => registration.AddConsumerWithPolicy<OutboxProducedConsumer>(
                $"{prefix}-outbox-produced",
                new ConsumerDeliveryPolicyOptions
                {
                    RetryIntervals = [TimeSpan.FromMilliseconds(20)],
                    RedeliveryIntervals = [TimeSpan.FromMilliseconds(100)],
                    PrefetchCount = 1,
                    ConcurrentMessageLimit = 1,
                    IsCritical = true
                }));
        return builder.Build();
    }

    private static async Task RecreateDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static async Task WaitForOutboxDrainAsync(IServiceProvider services)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
            if (!await dbContext.Set<OutboxMessage>().AnyAsync())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException("Pending outbox messages were not delivered after recovery.");
    }

    private static async Task SetDatabaseConnectionsAllowedAsync(
        string applicationConnectionString,
        bool allowed)
    {
        var application = new NpgsqlConnectionStringBuilder(applicationConnectionString);
        var databaseName = application.Database;
        var admin = new NpgsqlConnectionStringBuilder(applicationConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var quotedDatabaseName = $"\"{databaseName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using (var alter = connection.CreateCommand())
        {
            alter.CommandText = $"ALTER DATABASE {quotedDatabaseName} WITH ALLOW_CONNECTIONS {(allowed ? "true" : "false")};";
            await alter.ExecuteNonQueryAsync();
        }

        if (allowed)
        {
            return;
        }

        await using var terminate = connection.CreateCommand();
        terminate.CommandText =
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
            "WHERE datname = @databaseName AND pid <> pg_backend_pid();";
        terminate.Parameters.AddWithValue("databaseName", databaseName);
        await terminate.ExecuteNonQueryAsync();
    }

    private static async Task WaitForCollectorRecoveryAsync(
        OutboxMetricsCollector<ReliabilityDbContext> collector)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            await collector.CollectAsync(CancellationToken.None);
            if (collector.IsHealthy)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("Outbox collector did not recover after database access was restored.");
    }

    private static async Task StopIgnoringBrokerFailureAsync(IHost host)
    {
        try
        {
            await host.StopAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // The test deliberately terminates the process while the broker is unavailable.
        }
    }

    private static string RabbitConnectionString(IContainer rabbit) =>
        $"amqp://guest:guest@{rabbit.Hostname}:{rabbit.GetMappedPublicPort(5672)}/";

    private static ushort ReserveUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable '{name}' is required.");
}
