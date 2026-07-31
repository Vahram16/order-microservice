using Microservices.Contracts;
using Microservices.Messaging;

namespace Microservices.Messaging.Tests;

public sealed class IntegrationMessageIdentityTests
{
    [Fact]
    public void ValidationAcceptsCompleteStableIdentity()
    {
        var message = new TestIntegrationMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ContractVersion: 1);

        IntegrationMessageIdentity.Validate(message);
    }

    [Fact]
    public void ValidationRejectsEmptyMessageIdAsPermanentFailure()
    {
        var message = ValidMessage() with { MessageId = Guid.Empty };

        var exception = Assert.Throws<IntegrationMessageIdentityException>(() =>
            IntegrationMessageIdentity.Validate(message));

        Assert.IsAssignableFrom<IPermanentConsumerFailure>(exception);
    }

    [Fact]
    public void ValidationRejectsEmptyCorrelationIdAsPermanentFailure()
    {
        var message = ValidMessage() with { CorrelationId = Guid.Empty };

        var exception = Assert.Throws<IntegrationMessageIdentityException>(() =>
            IntegrationMessageIdentity.Validate(message));

        Assert.IsAssignableFrom<IPermanentConsumerFailure>(exception);
    }

    [Fact]
    public void ValidationRejectsEmptyCausationIdAsPermanentFailure()
    {
        var message = ValidMessage() with { CausationId = Guid.Empty };

        var exception = Assert.Throws<IntegrationMessageIdentityException>(() =>
            IntegrationMessageIdentity.Validate(message));

        Assert.IsAssignableFrom<IPermanentConsumerFailure>(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidationRejectsNonPositiveContractVersion(int contractVersion)
    {
        var message = ValidMessage() with { ContractVersion = contractVersion };

        var exception = Assert.Throws<IntegrationMessageIdentityException>(() =>
            IntegrationMessageIdentity.Validate(message));

        Assert.IsAssignableFrom<IPermanentConsumerFailure>(exception);
    }

    private static TestIntegrationMessage ValidMessage() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            ContractVersion: 1);

    private sealed record TestIntegrationMessage(
        Guid MessageId,
        Guid CorrelationId,
        Guid? CausationId,
        int ContractVersion) : IIntegrationMessage;
}
