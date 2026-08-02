using System.Diagnostics;
using System.Globalization;
using System.Text;
using MassTransit.EntityFrameworkCoreIntegration;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Microservices.Messaging.Tests;

[Collection(MessagingBehaviorTestGroup.Name)]
public sealed class OutboxMonitoringPostgresTests
{
    private const int BusMessages = 20_000;
    private const int ConsumerMessages = 20_000;

    [Fact]
    public async Task CollectorCountsPendingRolesAndIndexesSupportOldestMessageLookup()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18")
            .WithDatabase("outbox_monitoring")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();

        using var services = BuildServices(postgres.GetConnectionString());
        await CreateSchemaAndBacklogAsync(services);

        var collector = CreateCollector(services);
        var elapsed = Stopwatch.StartNew();
        await collector.CollectAsync(CancellationToken.None);

        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(10),
            elapsed.Elapsed.ToString("c", CultureInfo.InvariantCulture));
        Assert.True(collector.IsHealthy);
        Assert.Collection(
            collector.CurrentSnapshots.OrderBy(snapshot => snapshot.Role),
            bus =>
            {
                Assert.Equal(OutboxMetricsCollector<ReliabilityDbContext>.OutboxRole.Bus, bus.Role);
                Assert.Equal(BusMessages, bus.Count);
                Assert.True(bus.OldestAgeSeconds > 0);
            },
            consumer =>
            {
                Assert.Equal(
                    OutboxMetricsCollector<ReliabilityDbContext>.OutboxRole.Consumer,
                    consumer.Role);
                Assert.Equal(ConsumerMessages, consumer.Count);
                Assert.True(consumer.OldestAgeSeconds > 0);
            });

        var health = await new OutboxCollectorHealthCheck<ReliabilityDbContext>(collector)
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, health.Status);

        // PostgreSQL may correctly prefer a sequential scan for an exact aggregate that touches
        // every pending row. The latency-sensitive operation is locating the oldest row; validate
        // that each partial index is planner-usable for that bounded lookup.
        var busPlan = await ExplainAsync(
            services,
            """
            SELECT "SentTime"
            FROM "OutboxMessage"
            WHERE "OutboxId" IS NOT NULL
            ORDER BY "SentTime"
            LIMIT 1;
            """);
        Assert.Contains("IX_OutboxMessage_BusPending_SentTime", busPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on \"OutboxMessage\"", busPlan, StringComparison.Ordinal);

        var consumerPlan = await ExplainAsync(
            services,
            """
            SELECT "SentTime"
            FROM "OutboxMessage"
            WHERE "OutboxId" IS NULL
              AND "InboxMessageId" IS NOT NULL
              AND "InboxConsumerId" IS NOT NULL
            ORDER BY "SentTime"
            LIMIT 1;
            """);
        Assert.Contains(
            "IX_OutboxMessage_ConsumerPending_SentTime",
            consumerPlan,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on \"OutboxMessage\"", consumerPlan, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReliabilityDbContext>(options => options.UseNpgsql(connectionString));
        return services.BuildServiceProvider();
    }

    private static OutboxMetricsCollector<ReliabilityDbContext> CreateCollector(
        ServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<ILogger<OutboxMetricsCollector<ReliabilityDbContext>>>(),
            new RabbitMqMessagingOptions
            {
                OutboxMetricsInterval = TimeSpan.FromMinutes(1),
                OutboxMetricsQueryTimeout = TimeSpan.FromSeconds(10)
            });

    private static async Task CreateSchemaAndBacklogAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX "IX_OutboxMessage_BusPending_SentTime"
                ON "OutboxMessage" ("SentTime", "OutboxId")
                WHERE "OutboxId" IS NOT NULL;

            CREATE INDEX "IX_OutboxMessage_ConsumerPending_SentTime"
                ON "OutboxMessage" ("SentTime", "InboxMessageId", "InboxConsumerId")
                WHERE "OutboxId" IS NULL
                  AND "InboxMessageId" IS NOT NULL
                  AND "InboxConsumerId" IS NOT NULL;
            """);

        var busOutboxId = Guid.NewGuid();
        var deliveredCleanupOnlyOutboxId = Guid.NewGuid();
        var inboxMessageId = Guid.NewGuid();
        var inboxConsumerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "OutboxState" ("OutboxId", "Created", "LockId")
            VALUES ({busOutboxId}, {now}, {Guid.NewGuid()});

            INSERT INTO "OutboxState" ("OutboxId", "Created", "Delivered", "LockId")
            VALUES ({deliveredCleanupOnlyOutboxId}, {now}, {now}, {Guid.NewGuid()});

            INSERT INTO "InboxState"
                ("MessageId", "ConsumerId", "LockId", "ReceiveCount", "Received", "Consumed", "Delivered")
            VALUES
                ({inboxMessageId}, {inboxConsumerId}, {Guid.NewGuid()}, 1, {now}, {now}, {now});
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "OutboxMessage"
                ("Body", "ContentType", "MessageId", "MessageType", "SentTime", "OutboxId")
            SELECT
                json_build_object()::text,
                'application/vnd.masstransit+json',
                gen_random_uuid(),
                'urn:message:tests:BusPending',
                {now} - make_interval(secs => series),
                {busOutboxId}
            FROM generate_series(1, {BusMessages}) AS series;
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "OutboxMessage"
                ("Body", "ContentType", "MessageId", "MessageType", "SentTime",
                 "InboxMessageId", "InboxConsumerId")
            SELECT
                json_build_object()::text,
                'application/vnd.masstransit+json',
                gen_random_uuid(),
                'urn:message:tests:ConsumerPending',
                {now} - make_interval(secs => series),
                {inboxMessageId},
                {inboxConsumerId}
            FROM generate_series(1, {ConsumerMessages}) AS series;
            """);

        await dbContext.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) \"OutboxMessage\";");
    }

    private static async Task<string> ExplainAsync(
        ServiceProvider services,
        string query)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {query}";
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new StringBuilder();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture));
        }

        return plan.ToString();
    }
}
