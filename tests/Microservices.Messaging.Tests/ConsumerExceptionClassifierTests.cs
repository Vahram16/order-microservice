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
        var services = new ServiceCollection();
        services.AddSingleton<IConsumerExceptionRule>(new TestRule());
        services.AddSingleton<IConsumerExceptionClassifier, ConsumerExceptionClassifier>();

        using var provider = services.BuildServiceProvider();
        var classifier = provider.GetRequiredService<IConsumerExceptionClassifier>();

        Assert.True(classifier.IsTransient(new ServiceSpecificException("dependency-temporary")));
        Assert.False(classifier.IsTransient(new ServiceSpecificException("dependency-invalid")));
    }

    [Fact]
    public void WrappedDependencyFailureIsEvaluatedByServiceRule()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsumerExceptionRule>(new TestRule());
        services.AddSingleton<IConsumerExceptionClassifier, ConsumerExceptionClassifier>();

        using var provider = services.BuildServiceProvider();
        var classifier = provider.GetRequiredService<IConsumerExceptionClassifier>();
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

    private static IConsumerExceptionClassifier CreateClassifier()
    {
        var services = new ServiceCollection();
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

    private sealed class ServiceSpecificException(string code) : Exception
    {
        public string Code { get; } = code;
    }

    private sealed class MarkedTransientException : Exception, ITransientConsumerFailure;

    private sealed class MarkedPermanentException : Exception, IPermanentConsumerFailure;

    private sealed class OutcomeUnknownException : Exception, IOutcomeUnknownConsumerFailure;
}
