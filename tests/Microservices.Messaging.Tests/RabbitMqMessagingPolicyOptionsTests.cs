using Microservices.Messaging;
using Microsoft.Extensions.Options;

namespace Microservices.Messaging.Tests;

public sealed class RabbitMqMessagingPolicyOptionsTests
{
    private const string SecureConnectionString =
        "amqps://guest:secret@rabbitmq.example:5671/";

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

        var exception = Assert.Throws<OptionsValidationException>(() =>
            RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
                options,
                SecureConnectionString));

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "must configure both RateLimit and RateLimitInterval",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationRejectsSingleActiveConsumerWithoutSerialLimits()
    {
        var options = CreateOptions();
        options.Consumers.Add(
            "orders-ordered-command",
            new ConsumerDeliveryPolicyOptions
            {
                SingleActiveConsumer = true,
                PrefetchCount = 2,
                ConcurrentMessageLimit = 1
            });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
                options,
                SecureConnectionString));

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "PrefetchCount=1 and ConcurrentMessageLimit=1",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationAcceptsSingleActiveConsumerWithSerialLimits()
    {
        var options = CreateOptions();
        options.Consumers.Add(
            "orders-ordered-command",
            new ConsumerDeliveryPolicyOptions
            {
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
    public void ValidationRejectsPayloadLimitAboveQueueByteLimit()
    {
        var options = CreateOptions(
            queueMaxLengthBytes: 1_024,
            maximumMessageBytes: 2_048);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
                options,
                SecureConnectionString));

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

        var exception = Assert.Throws<OptionsValidationException>(() =>
            RabbitMqMessagingOptionsValidator.ValidateAndGetHostAddress(
                options,
                SecureConnectionString));

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                nameof(RabbitMqMessagingOptions.ConsumerStopTimeout),
                StringComparison.Ordinal) &&
                failure.Contains(
                    nameof(RabbitMqMessagingOptions.StopTimeout),
                    StringComparison.Ordinal));
    }

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
