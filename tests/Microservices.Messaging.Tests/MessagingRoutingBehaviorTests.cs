namespace Microservices.Messaging.Tests;

[Collection(MessagingBehaviorTestGroup.Name)]
public sealed class MessagingRoutingBehaviorTests(MessagingReliabilityFixture fixture)
{
    [Fact]
    public async Task EventPublisherFansOutToEverySubscribedEndpoint()
    {
        var message = ReliabilityMessageFactory.RoutedEvent();

        await fixture.PublishEventAsync(message);
        await fixture.WaitForStableCompletionCountAsync(message.MessageId, 2);

        Assert.Equal(2, fixture.Probe.CompletionCount(message.MessageId));
    }

    [Fact]
    public async Task CommandSenderTargetsOnlyTheConfiguredOwningEndpoint()
    {
        var command = ReliabilityMessageFactory.RoutedCommand();

        await fixture.SendCommandAsync(command);
        await fixture.WaitForStableCompletionCountAsync(command.MessageId, 1);

        Assert.Equal(1, fixture.Probe.CompletionCount(command.MessageId));
    }
}
