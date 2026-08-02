using Microservices.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microservices.Messaging.Tests;

public sealed class RabbitMqMessagingPolicyOptionsTests
{
    private const string SecureConnectionString =
        "amqps://guest:secret@rabbitmq.example:5671/";

    [Fact]
    public void RemovedReceiveQueueTtlConfigurationFailsStartupValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:QueueMessageTimeToLive"] = "1.00:00:00"
            })
            .Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            RabbitMqMessagingOptionsValidator.RejectRemovedConfiguration(configuration));

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("x-message-ttl", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsIncompleteConsumerRateLimit()
    {
        var options = CreateOptions();
        options.Consumers.Add(
            "orders-sensitive-dependency",
            new ConsumerDeliveryPolicyOptions
            {
                RateLimit = 10
            });

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "must configure both RateLimit and RateLimitInterval",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsOrderingPolicyWithoutSerialBrokerSemantics()
    {
        var options = CreateOptions();
        options.Consumers.Add(
            "orders-ordered-command",
            new ConsumerDeliveryPolicyOptions
            {
                RequiresOrderedDelivery = true,
                SingleActiveConsumer = false,
                PrefetchCount = 1,
                ConcurrentMessageLimit = 1
            });

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("ordering-sensitive", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationAcceptsOrderingPolicyWithSerialLimits()
    {
        var options = CreateOptions();
        options.Consumers.Add(
            "orders-ordered-command",
            new ConsumerDeliveryPolicyOptions
            {
                RequiresOrderedDelivery = true,
                SingleActiveConsumer = true,
                PrefetchCount = 1,
                ConcurrentMessageLimit = 1
            });

        var address = RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
            options,
            SecureConnectionString);

        Assert.Equal(new Uri(SecureConnectionString), address);
    }

    [Fact]
    public void CriticalConsumerMustDeclareConcurrencyExplicitly()
    {
        var options = CreateOptions();
        options.Consumers.Add(
            "orders-critical-command",
            new ConsumerDeliveryPolicyOptions
            {
                IsCritical = true
            });

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("must explicitly configure", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Orders-Consumer")]
    [InlineData("orders--consumer")]
    [InlineData("orders_consumer")]
    [InlineData("-orders-consumer")]
    public void ValidationRejectsInvalidStableEndpointName(string endpointName)
    {
        var options = CreateOptions();
        options.Consumers.Add(endpointName, new ConsumerDeliveryPolicyOptions());

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("lowercase kebab-case", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsRetryAndRedeliveryDelayAboveConfiguredMaximum()
    {
        var options = new RabbitMqMessagingOptions
        {
            MaximumRetryAndRedeliveryDelay = TimeSpan.FromSeconds(5),
            RetryIntervals = [TimeSpan.FromSeconds(2)],
            RedeliveryIntervals = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)]
        };

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("exceeding", StringComparison.Ordinal));
    }

    [Fact]
    public void DelayCalculationIncludesRetrySequenceForEveryRedelivery()
    {
        var total = RabbitMqMessagingOptionsValidator.CalculateRetryAndRedeliveryDelay(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);

        Assert.Equal(TimeSpan.FromSeconds(24), total);
    }

    [Fact]
    public void ValidationRejectsPayloadLimitAboveQueueByteLimit()
    {
        var options = CreateOptions(
            queueMaxLengthBytes: 1_024,
            maximumMessageBytes: 2_048);

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                nameof(RabbitMqMessagingOptions.MaximumMessageBytes),
                StringComparison.Ordinal) &&
                failure.Contains(
                    nameof(RabbitMqMessagingOptions.QueueMaxLengthBytes),
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsConsumerStopTimeoutAboveStopTimeout()
    {
        var options = CreateOptions(
            stopTimeout: TimeSpan.FromSeconds(10),
            consumerStopTimeout: TimeSpan.FromSeconds(11));

        var exception = ValidateFailure(options);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                nameof(RabbitMqMessagingOptions.ConsumerStopTimeout),
                StringComparison.Ordinal) &&
                failure.Contains(
                    nameof(RabbitMqMessagingOptions.StopTimeout),
                    StringComparison.Ordinal));
    }

    private static OptionsValidationException ValidateFailure(RabbitMqMessagingOptions options) =>
        Assert.Throws<OptionsValidationException>(() =>
            RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
                options,
                SecureConnectionString));

    private static RabbitMqMessagingOptions CreateOptions(
        long queueMaxLengthBytes = 1_073_741_824,
        int maximumMessageBytes = 1_048_576,
        TimeSpan? stopTimeout = null,
        TimeSpan? consumerStopTimeout = null) =>
        new()
        {
            QueueMaxLengthBytes = queueMaxLengthBytes,
            MaximumMessageBytes = maximumMessageBytes,
            StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(30),
            ConsumerStopTimeout = consumerStopTimeout ?? TimeSpan.FromSeconds(25)
        };
}
