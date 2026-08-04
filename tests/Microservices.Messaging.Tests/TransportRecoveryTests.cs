using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microservices.Messaging.Tests;

[Collection(MessagingBehaviorTestGroup.Name)]
public sealed class TransportRecoveryTests(MessagingReliabilityFixture fixture)
{
    [Fact]
    public async Task RabbitMqDisconnectDuringPublishRecoversAndDeliversExactlyOnce()
    {
        var message = ReliabilityMessageFactory.Success();

        await DisconnectAllRabbitMqClientsAsync();
        var publish = fixture.PublishAsync(message);

        await fixture.RabbitMq.WaitForConnectionsAsync(1);
        await publish.WaitAsync(TimeSpan.FromSeconds(15));
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);
        await fixture.WaitForStableEffectCountAsync(message.MessageId, 1);

        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task PostgreSqlBackendTerminationUsesServiceOwnedRetryRuleAndRecovers()
    {
        var prefix = $"postgres-recovery-{Guid.NewGuid():N}"[..46];
        var endpoint = $"{prefix}-terminated-backend";
        var probe = new DeliveryProbe();
        using var host = BuildDatabaseRecoveryHost(prefix, endpoint, probe);
        await host.StartAsync();

        var message = new DatabaseTerminationMessage(Guid.NewGuid());
        var bus = host.Services.GetRequiredService<IBus>();
        await bus.Publish(message);
        await probe.WaitForCompletionAsync(message.MessageId);

        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReliabilityDbContext>();
        var effectCount = await dbContext.Effects
            .CountAsync(effect => effect.Id == message.MessageId);

        Assert.Equal(2, probe.AttemptCount(message.MessageId));
        Assert.Equal(1, effectCount);
        await host.StopAsync();
    }

    private static IHost BuildDatabaseRecoveryHost(
        string prefix,
        string endpoint,
        DeliveryProbe probe)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = RequiredEnvironmentVariable(
                "MESSAGING_TEST_RABBITMQ_CONNECTION_STRING"),
            ["Messaging:UseTls"] = "false",
            ["Messaging:RetryIntervals:0"] = "00:00:00.100",
            ["Messaging:RedeliveryIntervals:0"] = "00:00:00.500",
            ["Messaging:PrefetchCount"] = "1",
            ["Messaging:ConcurrentMessageLimit"] = "1",
            ["Messaging:StartTimeout"] = "00:00:15",
            ["Messaging:StopTimeout"] = "00:00:10",
            ["Messaging:ConsumerStopTimeout"] = "00:00:08",
            ["Messaging:OutboxQueryDelay"] = "00:00:00.050",
            ["Messaging:OutboxMetricsInterval"] = "00:00:00.250",
            ["Messaging:DuplicateDetectionWindow"] = "00:05:00",
            ["Messaging:FaultQueueRetention"] = "00:30:00",
            ["Messaging:QueueMaxLength"] = "1000",
            ["Messaging:QueueMaxLengthBytes"] = "10485760",
            ["Messaging:MaximumMessageBytes"] = "1048576",
            ["Messaging:FaultQueueMaxLength"] = "1000"
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddSingleton(probe);
        builder.Services.AddSingleton<IConsumerExceptionRule, TestPostgresTransientRule>();
        builder.Services.AddDbContext<ReliabilityDbContext>(options =>
            options.UseNpgsql(
                RequiredEnvironmentVariable("MESSAGING_TEST_POSTGRES_CONNECTION_STRING"),
                npgsql => npgsql.EnableRetryOnFailure()));
        builder.Services.AddRabbitMqWithPostgresOutbox<ReliabilityDbContext>(
            builder.Configuration,
            prefix,
            registration =>
            {
                var consumer = registration.AddConsumer<DatabaseTerminationConsumer>();
                consumer.Endpoint(configuration => configuration.Name = endpoint);
            });
        return builder.Build();
    }

    private static async Task DisconnectAllRabbitMqClientsAsync()
    {
        using var client = CreateRabbitMqManagementClient();
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

            using var delete = await client.DeleteAsync(
                $"/api/connections/{Uri.EscapeDataString(name)}");
            delete.EnsureSuccessStatusCode();
        }
    }

    private static HttpClient CreateRabbitMqManagementClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(
                RequiredEnvironmentVariable("MESSAGING_TEST_RABBITMQ_API"),
                UriKind.Absolute)
        };
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $"{RequiredEnvironmentVariable("MESSAGING_TEST_RABBITMQ_USERNAME")}:" +
                RequiredEnvironmentVariable("MESSAGING_TEST_RABBITMQ_PASSWORD")));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        return client;
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Environment variable '{name}' is required."));

    private sealed class TestPostgresTransientRule : IConsumerExceptionRule
    {
        public ConsumerExceptionDisposition Classify(Exception exception) =>
            exception is DbException { IsTransient: true }
                ? ConsumerExceptionDisposition.Transient
                : ConsumerExceptionDisposition.Unclassified;
    }
}

public sealed record DatabaseTerminationMessage(Guid MessageId);

public sealed class DatabaseTerminationConsumer(
    DeliveryProbe probe,
    ReliabilityDbContext dbContext) : IConsumer<DatabaseTerminationMessage>
{
    public async Task Consume(ConsumeContext<DatabaseTerminationMessage> context)
    {
        var attempt = probe.RecordAttempt(context.Message.MessageId);
        if (attempt == 1)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_terminate_backend(pg_backend_pid())",
                context.CancellationToken);
            throw new InvalidOperationException(
                "PostgreSQL did not terminate the current backend as requested by the test.");
        }

        dbContext.Effects.Add(new ReliabilityEffect(context.Message.MessageId, 1));
        await dbContext.SaveChangesAsync(context.CancellationToken);
        probe.Complete(context.Message.MessageId);
    }
}
