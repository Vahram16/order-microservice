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
    public async Task SuccessfulMessageIsConsumedOnce()
    {
        var message = ReliabilityMessageFactory.Success();

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        Assert.Equal(1, fixture.Probe.AttemptCount(message.MessageId));
        Assert.Equal(1, await fixture.GetEffectCountAsync(message.MessageId));
    }

    [Fact]
    public async Task TransientFailureUsesBoundedImmediateRetry()
    {
        var message = ReliabilityMessageFactory.OneRetry();

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        Assert.Equal(2, fixture.Probe.AttemptCount(message.MessageId));
    }

    [Fact]
    public async Task TransientFailureUsesBrokerBackedRedeliveryAfterRetry()
    {
        var message = ReliabilityMessageFactory.RedeliverySuccess();

        await fixture.PublishAsync(message);
        await fixture.Probe.WaitForCompletionAsync(message.MessageId);

        Assert.Equal(3, fixture.Probe.AttemptCount(message.MessageId));
    }

    [Fact]
    public async Task ExhaustedTransientFailureReachesErrorQueue()
    {
        var message = ReliabilityMessageFactory.Exhausted();
        var endpoint = fixture.Endpoint<ExhaustedConsumer>();
        var initialErrorDepth = await fixture.RabbitMq.QueueDepthAsync($"{endpoint}_error");

        await fixture.PublishAsync(message);
        await fixture.RabbitMq.WaitForQueueDepthAsync($"{endpoint}_error", initialErrorDepth + 1);

        Assert.Equal(6, fixture.Probe.AttemptCount(message.MessageId));
    }

    [Fact]
    public async Task PermanentFailureDoesNotRetryAndReachesErrorQueue()
    {
        var message = ReliabilityMessageFactory.Permanent();
        var endpoint = fixture.Endpoint<PermanentConsumer>();
        var initialErrorDepth = await fixture.RabbitMq.QueueDepthAsync($"{endpoint}_error");

        await fixture.PublishAsync(message);
        await fixture.RabbitMq.WaitForQueueDepthAsync($"{endpoint}_error", initialErrorDepth + 1);

        Assert.Equal(1, fixture.Probe.AttemptCount(message.MessageId));
    }

    [Fact]
    public async Task UnconsumedMessageIsPlacedInSkippedQueue()
    {
        var endpoint = fixture.Endpoint<SuccessConsumer>();
        var skippedQueue = $"{endpoint}_skipped";
        var initialSkippedDepth = await fixture.RabbitMq.QueueDepthAsync(skippedQueue);

        await fixture.SendToEndpointAsync(endpoint, new UnsupportedTestMessage(Guid.NewGuid()));
        await fixture.RabbitMq.WaitForQueueDepthAsync(skippedQueue, initialSkippedDepth + 1);
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
    public async Task BusOutboxRollbackProducesNoMessageAndCommitProducesOne()
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
}
