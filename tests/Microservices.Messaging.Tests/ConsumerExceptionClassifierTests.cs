using Microservices.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.Messaging.Tests;

public sealed class ConsumerExceptionClassifierTests
{
    [Fact]
    public void ExplicitTransientMarkerIsRetryable()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerExceptionDisposition.Transient,
            classifier.Classify(new MarkedTransientException()));
    }

    [Fact]
    public void UnknownFailureIsPermanentByDefault()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerExceptionDisposition.Permanent,
            classifier.Classify(new InvalidOperationException("unknown")));
    }

    [Fact]
    public void CancellationIsSeparateAndNeverRetried()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerExceptionDisposition.Cancelled,
            classifier.Classify(new OperationCanceledException()));
    }

    [Fact]
    public void ServiceOwnedRuleClassifiesDependencySpecificFailures()
    {
        var classifier = CreateClassifier(new TestRule());

        Assert.True(classifier.IsTransient(new ServiceSpecificException("dependency-temporary")));
        Assert.False(classifier.IsTransient(new ServiceSpecificException("dependency-invalid")));
    }

    [Fact]
    public void WrappedDependencyFailureIsEvaluatedByServiceRule()
    {
        var classifier = CreateClassifier(new TestRule());
        var exception = new InvalidOperationException(
            "provider wrapper",
            new ServiceSpecificException("dependency-temporary"));

        Assert.Equal(ConsumerExceptionDisposition.Transient, classifier.Classify(exception));
    }

    [Fact]
    public void PermanentFailureTakesPrecedenceOverTransientFailure()
    {
        var classifier = CreateClassifier();
        var exception = new AggregateException(
            new MarkedTransientException(),
            new MarkedPermanentException());

        Assert.Equal(ConsumerExceptionDisposition.Permanent, classifier.Classify(exception));
    }

    [Fact]
    public void OutcomeUnknownIsPermanentWithoutExplicitSafeReplayRule()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerExceptionDisposition.Permanent,
            classifier.Classify(new OutcomeUnknownException()));
    }

    [Fact]
    public void OutcomeUnknownCanBeRetriedWhenServiceRuleProvesSafeReplay()
    {
        var classifier = CreateClassifier(new SafeReplayRule());

        Assert.Equal(
            ConsumerExceptionDisposition.Transient,
            classifier.Classify(new OutcomeUnknownException()));
    }

    private static IConsumerExceptionClassifier CreateClassifier(
        IConsumerExceptionRule? rule = null)
    {
        var services = new ServiceCollection();
        if (rule is not null)
        {
            services.AddSingleton(rule);
            services.AddSingleton<IConsumerExceptionRule>(provider => provider.GetRequiredService(rule.GetType()) as IConsumerExceptionRule
                ?? throw new InvalidOperationException("The test rule does not implement IConsumerExceptionRule."));
        }

        services.AddSingleton<IConsumerExceptionClassifier, ConsumerExceptionClassifier>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IConsumerExceptionClassifier>();
    }

    private sealed class TestRule : IConsumerExceptionRule
    {
        public ConsumerExceptionDisposition Classify(Exception exception) =>
            exception is ServiceSpecificException { Code: "dependency-temporary" }
                ? ConsumerExceptionDisposition.Transient
                : exception is ServiceSpecificException
                    ? ConsumerExceptionDisposition.Permanent
                    : ConsumerExceptionDisposition.Unclassified;
    }

    private sealed class SafeReplayRule : IConsumerExceptionRule
    {
        public ConsumerExceptionDisposition Classify(Exception exception) =>
            exception is OutcomeUnknownException
                ? ConsumerExceptionDisposition.Transient
                : ConsumerExceptionDisposition.Unclassified;
    }

    private sealed class ServiceSpecificException(string code) : Exception
    {
        public string Code { get; } = code;
    }

    private sealed class MarkedTransientException : Exception, ITransientConsumerFailure;

    private sealed class MarkedPermanentException : Exception, IPermanentConsumerFailure;

    private sealed class OutcomeUnknownException : Exception, IOutcomeUnknownConsumerFailure;
}
