using Microservices.Messaging;
using Microsoft.Extensions.Options;

namespace Microservices.Messaging.Tests;

public sealed class ConsumerPolicyRegistryTests
{
    private static readonly ConsumerDeliveryPolicyOptions ExplicitPolicy = new()
    {
        PrefetchCount = 1,
        ConcurrentMessageLimit = 1,
        IsCritical = true
    };

    [Fact]
    public void MisspelledLegacyPolicyFailsWhenNoEndpointMatches()
    {
        var options = new RabbitMqMessagingOptions
        {
            Consumers = new Dictionary<string, ConsumerDeliveryPolicyOptions>(StringComparer.Ordinal)
            {
                ["orders-misspelled"] = ExplicitPolicy
            }
        };
        var registry = new ConsumerPolicyRegistry(options);
        registry.Resolve("orders-actual");

        var exception = Assert.Throws<OptionsValidationException>(
            registry.ValidateAllPoliciesMatched);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("orders-misspelled", StringComparison.Ordinal) &&
                       failure.Contains("matches no registered receive endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsumerRenameCannotSilentlyDropTypedPolicy()
    {
        var registry = new ConsumerPolicyRegistry(new RabbitMqMessagingOptions());
        registry.Add(typeof(CriticalConsumerFixture), "orders-v1-critical", ExplicitPolicy);
        registry.Resolve("orders-v2-critical");

        var exception = Assert.Throws<OptionsValidationException>(
            registry.ValidateAllPoliciesMatched);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(typeof(CriticalConsumerFixture).FullName!, StringComparison.Ordinal) &&
                       failure.Contains("orders-v1-critical", StringComparison.Ordinal));
    }

    [Fact]
    public void EndpointNameCollisionIdentifiesBothConsumers()
    {
        var registry = new ConsumerPolicyRegistry(new RabbitMqMessagingOptions());
        registry.Add(typeof(CriticalConsumerFixture), "orders-critical", ExplicitPolicy);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            registry.Add(typeof(AnotherConsumerFixture), "orders-critical", ExplicitPolicy));

        var failure = Assert.Single(exception.Failures);
        Assert.Contains(typeof(CriticalConsumerFixture).FullName!, failure, StringComparison.Ordinal);
        Assert.Contains(typeof(AnotherConsumerFixture).FullName!, failure, StringComparison.Ordinal);
        Assert.Contains("orders-critical", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void UnapprovedDefaultPolicyFailsImmediately()
    {
        var registry = new ConsumerPolicyRegistry(new RabbitMqMessagingOptions
        {
            AllowValidatedDefaultConsumerPolicy = false
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            registry.Resolve("orders-unconfigured"));

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("AddConsumerWithPolicy", StringComparison.Ordinal) &&
                       failure.Contains("orders-unconfigured", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitTypedPolicyUsesStableNameIndependentOfClrTypeName()
    {
        var registry = new ConsumerPolicyRegistry(new RabbitMqMessagingOptions());
        registry.Add(typeof(CriticalConsumerFixture), "orders-stable-business-name", ExplicitPolicy);

        var resolved = registry.Resolve("orders-stable-business-name");
        registry.ValidateAllPoliciesMatched();

        Assert.Same(ExplicitPolicy, resolved);
        Assert.DoesNotContain(
            nameof(CriticalConsumerFixture),
            "orders-stable-business-name",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CriticalConsumerFixture;

    private sealed class AnotherConsumerFixture;
}
