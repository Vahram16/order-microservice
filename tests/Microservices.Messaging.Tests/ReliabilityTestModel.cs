using System.Collections.Concurrent;
using MassTransit;
using Microservices.Contracts;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Microservices.Messaging.Tests;

public sealed class ReliabilityDbContext(DbContextOptions<ReliabilityDbContext> options)
    : DbContext(options)
{
    public DbSet<ReliabilityEffect> Effects => Set<ReliabilityEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ReliabilityEffect>(entity =>
        {
            entity.HasKey(effect => effect.Id);
            entity.Property(effect => effect.Count);
        });
        modelBuilder.AddMassTransitOutboxEntities();
    }
}

public sealed class ReliabilityEffect(Guid id, int count)
{
    public Guid Id { get; private set; } = id;

    public int Count { get; private set; } = count;
}

public sealed class DeliveryProbe
{
    private readonly ConcurrentDictionary<Guid, int> _attempts = [];
    private readonly ConcurrentDictionary<Guid, int> _completions = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _completionSignals = [];

    public int RecordAttempt(Guid messageId) =>
        _attempts.AddOrUpdate(messageId, 1, static (_, count) => count + 1);

    public void Complete(Guid messageId)
    {
        _completions.AddOrUpdate(messageId, 1, static (_, count) => count + 1);
        _completionSignals.GetOrAdd(
            messageId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();
    }

    public int AttemptCount(Guid messageId) =>
        _attempts.GetValueOrDefault(messageId);

    public int CompletionCount(Guid messageId) =>
        _completions.GetValueOrDefault(messageId);

    public Task CompletionTask(Guid messageId) =>
        _completionSignals.GetOrAdd(
            messageId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .Task;

    public Task WaitForCompletionAsync(Guid messageId) =>
        CompletionTask(messageId).WaitAsync(TimeSpan.FromSeconds(15));
}

public sealed class SuccessConsumer(
    DeliveryProbe probe,
    ReliabilityDbContext dbContext) : IConsumer<SuccessMessage>
{
    public async Task Consume(ConsumeContext<SuccessMessage> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        dbContext.Effects.Add(new ReliabilityEffect(context.Message.MessageId, 1));
        await dbContext.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        probe.Complete(context.Message.MessageId);
    }
}

public sealed class OneRetryConsumer(DeliveryProbe probe) : IConsumer<OneRetryMessage>
{
    public Task Consume(ConsumeContext<OneRetryMessage> context)
    {
        if (probe.RecordAttempt(context.Message.MessageId) == 1)
        {
            throw new TestTransientException();
        }

        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class RedeliverySuccessConsumer(DeliveryProbe probe)
    : IConsumer<RedeliverySuccessMessage>
{
    public Task Consume(ConsumeContext<RedeliverySuccessMessage> context)
    {
        if (probe.RecordAttempt(context.Message.MessageId) <= 2)
        {
            throw new TestTransientException();
        }

        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class ExhaustedConsumer(DeliveryProbe probe) : IConsumer<ExhaustedMessage>
{
    public Task Consume(ConsumeContext<ExhaustedMessage> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        throw new TestTransientException();
    }
}

public sealed class PermanentConsumer(DeliveryProbe probe) : IConsumer<PermanentMessage>
{
    public Task Consume(ConsumeContext<PermanentMessage> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        throw new TestPermanentException();
    }
}

public sealed class DuplicateConsumer(
    DeliveryProbe probe,
    ReliabilityDbContext dbContext) : IConsumer<DuplicateMessage>
{
    public async Task Consume(ConsumeContext<DuplicateMessage> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        dbContext.Effects.Add(new ReliabilityEffect(context.Message.MessageId, 1));
        await dbContext.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        probe.Complete(context.Message.MessageId);
    }
}

public sealed class OutboxProducedConsumer(DeliveryProbe probe)
    : IConsumer<OutboxProducedMessage>
{
    public Task Consume(ConsumeContext<OutboxProducedMessage> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class FirstRoutedEventConsumer(DeliveryProbe probe)
    : IConsumer<RoutedEvent>
{
    public Task Consume(ConsumeContext<RoutedEvent> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class SecondRoutedEventConsumer(DeliveryProbe probe)
    : IConsumer<RoutedEvent>
{
    public Task Consume(ConsumeContext<RoutedEvent> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class PrimaryRoutedCommandConsumer(DeliveryProbe probe)
    : IConsumer<RoutedCommand>
{
    public Task Consume(ConsumeContext<RoutedCommand> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class SecondaryRoutedCommandConsumer(DeliveryProbe probe)
    : IConsumer<RoutedCommand>
{
    public Task Consume(ConsumeContext<RoutedCommand> context)
    {
        probe.RecordAttempt(context.Message.MessageId);
        probe.Complete(context.Message.MessageId);
        return Task.CompletedTask;
    }
}

public sealed class DrainConsumer(DrainGate gate) : IConsumer<DrainMessage>
{
    public async Task Consume(ConsumeContext<DrainMessage> context)
    {
        gate.Entered.TrySetResult();
        await gate.Release.Task.WaitAsync(context.CancellationToken).ConfigureAwait(false);
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

public sealed class TestTransientException : Exception, ITransientConsumerFailure;

public sealed class TestPermanentException : Exception, IPermanentConsumerFailure;

public sealed record SuccessMessage(Guid MessageId) : IIntegrationMessage;

public sealed record OneRetryMessage(Guid MessageId) : IIntegrationMessage;

public sealed record RedeliverySuccessMessage(Guid MessageId) : IIntegrationMessage;

public sealed record ExhaustedMessage(Guid MessageId) : IIntegrationMessage;

public sealed record PermanentMessage(Guid MessageId) : IIntegrationMessage;

public sealed record DuplicateMessage(Guid MessageId) : IIntegrationMessage;

public sealed record OutboxProducedMessage(
    Guid MessageId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record RoutedEvent(
    Guid MessageId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record RoutedCommand(Guid MessageId) : IIntegrationCommand;

public sealed record UnsupportedTestMessage(Guid MessageId);

public sealed record DrainMessage(Guid MessageId) : IIntegrationMessage;

internal static class ReliabilityMessageFactory
{
    public static SuccessMessage Success() => new(NewId.NextGuid());

    public static OneRetryMessage OneRetry() => new(NewId.NextGuid());

    public static RedeliverySuccessMessage RedeliverySuccess() => new(NewId.NextGuid());

    public static ExhaustedMessage Exhausted() => new(NewId.NextGuid());

    public static PermanentMessage Permanent() => new(NewId.NextGuid());

    public static DuplicateMessage Duplicate() => new(NewId.NextGuid());

    public static OutboxProducedMessage OutboxProduced() =>
        new(NewId.NextGuid(), DateTimeOffset.UtcNow);

    public static RoutedEvent RoutedEvent() =>
        new(NewId.NextGuid(), DateTimeOffset.UtcNow);

    public static RoutedCommand RoutedCommand() => new(NewId.NextGuid());
}
