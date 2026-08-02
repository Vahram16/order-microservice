using System.Diagnostics;
using Microservices.Messaging;

namespace Microservices.Messaging.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MessagingBehaviorTestGroup : ICollectionFixture<MessagingReliabilityFixture>
{
    public const string Name = "messaging-reliability";
}

[Collection(MessagingBehaviorTestGroup.Name)]
public sealed class MessagingFailureDeliveryBehaviorTests(MessagingReliabilityFixture fixture)
{
    [Fact]
    public async Task SuccessfulConsumptionRecordsOneInvocationAndNoFailureSignals()
    {
        var message = ReliabilityMessageFactory.Success();
        var endpoint = fixture.Endpoint<SuccessConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        var delta = await WaitForMetricDeltaAsync(baseline, endpoint, expectedAttemptDurations: 1);
        Assert.Equal(1, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(1, delta.AttemptDurations);
        Assert.Equal(0, delta.AttemptFailures);
        Assert.Equal(0, delta.ImmediateRetries);
        Assert.Equal(0, delta.RedeliveryDeliveries);
        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task OneImmediateRetryThenSuccessHasExactMetricSemantics()
    {
        var message = ReliabilityMessageFactory.OneRetry();
        var endpoint = fixture.Endpoint<OneRetryConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        var delta = await WaitForMetricDeltaAsync(baseline, endpoint, expectedAttemptDurations: 2);
        Assert.Equal(2, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(2, delta.AttemptDurations);
        Assert.Equal(1, delta.AttemptFailures);
        Assert.Equal(1, delta.ImmediateRetries);
        Assert.Equal(0, delta.RedeliveryDeliveries);
    }

    [Fact]
    public async Task MultipleImmediateRetriesThenSuccessHaveExactMetricSemantics()
    {
        var message = ReliabilityMessageFactory.MultipleRetries();
        var endpoint = fixture.Endpoint<MultipleRetryConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        var delta = await WaitForMetricDeltaAsync(baseline, endpoint, expectedAttemptDurations: 3);
        Assert.Equal(3, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(3, delta.AttemptDurations);
        Assert.Equal(2, delta.AttemptFailures);
        Assert.Equal(2, delta.ImmediateRetries);
        Assert.Equal(0, delta.RedeliveryDeliveries);
    }

    [Fact]
    public async Task DelayedRedeliveryThenSuccessIsNotCountedAsAnotherImmediateRetry()
    {
        var message = ReliabilityMessageFactory.RedeliverySuccess();
        var endpoint = fixture.Endpoint<RedeliverySuccessConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        var delta = await WaitForMetricDeltaAsync(baseline, endpoint, expectedAttemptDurations: 3);
        Assert.Equal(3, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(3, delta.AttemptDurations);
        Assert.Equal(2, delta.AttemptFailures);
        Assert.Equal(1, delta.ImmediateRetries);
        Assert.Equal(1, delta.RedeliveryDeliveries);
    }

    [Fact]
    public async Task ExhaustedRetryAndRedeliveryHasDeterministicErrorQueuePlacement()
    {
        var message = ReliabilityMessageFactory.Exhausted();
        var endpoint = fixture.Endpoint<ExhaustedConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);
        var initialErrorDepth = await fixture.RabbitMq.QueueDepthAsync($"{endpoint}_error");

        await fixture.PublishAsync(message);
        await fixture.RabbitMq.WaitForQueueDepthAsync($"{endpoint}_error", initialErrorDepth + 1);

        var delta = await WaitForMetricDeltaAsync(baseline, endpoint, expectedAttemptDurations: 6);
        Assert.Equal(6, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(6, delta.AttemptDurations);
        Assert.Equal(6, delta.AttemptFailures);
        Assert.Equal(3, delta.ImmediateRetries);
        Assert.Equal(2, delta.RedeliveryDeliveries);
    }

    [Fact]
    public async Task NonTransientFailureDoesNotRetryAndReachesErrorQueue()
    {
        var message = ReliabilityMessageFactory.Permanent();
        var endpoint = fixture.Endpoint<PermanentConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);
        var initialErrorDepth = await fixture.RabbitMq.QueueDepthAsync($"{endpoint}_error");

        await fixture.PublishAsync(message);
        await fixture.RabbitMq.WaitForQueueDepthAsync($"{endpoint}_error", initialErrorDepth + 1);

        var delta = await WaitForMetricDeltaAsync(baseline, endpoint, expectedAttemptDurations: 1);
        Assert.Equal(1, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(1, delta.AttemptDurations);
        Assert.Equal(1, delta.AttemptFailures);
        Assert.Equal(0, delta.ImmediateRetries);
        Assert.Equal(0, delta.RedeliveryDeliveries);
    }

    [Fact]
    public async Task UnconsumedMessageIsPlacedInSkippedQueueWithoutConsumerFailureMetric()
    {
        var endpoint = fixture.Endpoint<SuccessConsumer>();
        var baseline = fixture.Metrics.Snapshot(endpoint);
        var skippedQueue = $"{endpoint}_skipped";
        var initialSkippedDepth = await fixture.RabbitMq.QueueDepthAsync(skippedQueue);

        await fixture.SendToEndpointAsync(endpoint, new UnsupportedTestMessage(Guid.NewGuid()));
        await fixture.RabbitMq.WaitForQueueDepthAsync(skippedQueue, initialSkippedDepth + 1);

        var delta = fixture.Metrics.Delta(baseline, endpoint);
        Assert.Equal(0, delta.AttemptDurations);
        Assert.Equal(0, delta.AttemptFailures);
        Assert.Equal(0, delta.ImmediateRetries);
        Assert.Equal(0, delta.RedeliveryDeliveries);
    }

    [Fact]
    public async Task DuplicateTransportMessageIdDoesNotRepeatProtectedSideEffect()
    {
        var message = ReliabilityMessageFactory.Duplicate();
        var transportMessageId = Guid.NewGuid();

        await fixture.PublishAsync(message, transportMessageId);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);
        await fixture.PublishAsync(message, transportMessageId);
        await fixture.WaitForStableEffectCountAsync(message.MessageId, 1);

        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task BusOutboxRollbackProducesNoMessageAndCommitProducesExactlyOne()
    {
        var rolledBack = ReliabilityMessageFactory.OutboxProduced();
        var committed = ReliabilityMessageFactory.OutboxProduced();

        await fixture.ExecuteBusOutboxTransactionAsync(Guid.NewGuid(), rolledBack, commit: false);
        var rolledBackCompletion = fixture.Probe.CompletionTask(rolledBack.MessageId);
        var completedFirst = await Task.WhenAny(
            rolledBackCompletion,
            Task.Delay(TimeSpan.FromMilliseconds(750)));
        Assert.NotSame(rolledBackCompletion, completedFirst);

        await fixture.ExecuteBusOutboxTransactionAsync(Guid.NewGuid(), committed, commit: true);
        await fixture.Probe.WaitForCompletionAsync(committed.MessageId);
        await fixture.WaitForOutboxToDrainAsync();

        Assert.Equal(0, fixture.Probe.CompletionCount(rolledBack.MessageId));
        Assert.Equal(1, fixture.Probe.CompletionCount(committed.MessageId));
    }

    [Fact]
    public async Task CorrelationAndCausationPropagateWithoutPayloadCopying()
    {
        var parent = ReliabilityMessageFactory.Parent();
        var transportMessageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await fixture.PublishAsync(parent, transportMessageId, correlationId);
        var child = await fixture.Probe.WaitForChildAsync(parent.MessageId);

        Assert.Equal(correlationId, child.CorrelationId);
        Assert.Equal(transportMessageId, child.CausationId);
        Assert.NotEqual(transportMessageId, child.MessageId);
    }

    [Fact]
    public async Task DurableBusinessQueuesHaveCapacityLimitsButNoMessageTtl()
    {
        var arguments = await fixture.RabbitMq.QueueArgumentsAsync(
            fixture.Endpoint<SuccessConsumer>());

        Assert.DoesNotContain("x-message-ttl", arguments.Keys);
        Assert.Equal("reject-publish", arguments["x-overflow"]?.ToString());
        Assert.Equal(1_000L, Assert.IsType<long>(arguments["x-max-length"]));
        Assert.Equal(10_485_760L, Assert.IsType<long>(arguments["x-max-length-bytes"]));
    }

    [Fact]
    public Task GracefulShutdownCompletesInFlightConsumer() =>
        fixture.VerifyGracefulDrainAsync();

    private async Task<MessagingMetricSnapshot> WaitForMetricDeltaAsync(
        MessagingMetricSnapshot baseline,
        string endpoint,
        long expectedAttemptDurations)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            var delta = fixture.Metrics.Delta(baseline, endpoint);
            if (delta.AttemptDurations >= expectedAttemptDurations)
            {
                return delta;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        var finalDelta = fixture.Metrics.Delta(baseline, endpoint);
        throw new TimeoutException(
            $"Endpoint '{endpoint}' recorded {finalDelta.AttemptDurations} of " +
            $"{expectedAttemptDurations} expected consumer attempt durations.");
    }
}
