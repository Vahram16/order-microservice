using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microservices.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.Messaging.Tests;

public sealed class ConsumerExceptionClassifierTests
{
    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void SharedTransientFailuresAreRetryable(Exception exception)
    {
        var classifier = CreateClassifier();

        Assert.True(classifier.IsTransient(exception));
    }

    [Theory]
    [MemberData(nameof(PermanentExceptions))]
    public void SharedPermanentFailuresAreNotRetryable(Exception exception)
    {
        var classifier = CreateClassifier();

        Assert.False(classifier.IsTransient(exception));
    }

    [Fact]
    public void ServiceRuleOverridesSharedDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsumerExceptionRule>(new TestRule());
        services.AddSingleton<IConsumerExceptionClassifier, ConsumerExceptionClassifier>();

        using var provider = services.BuildServiceProvider();
        var classifier = provider.GetRequiredService<IConsumerExceptionClassifier>();

        Assert.True(classifier.IsTransient(new ServiceSpecificException()));
    }

    [Fact]
    public void PermanentMarkerTakesPrecedenceOverTransientMarker()
    {
        var classifier = CreateClassifier();

        Assert.False(classifier.IsTransient(new ConflictingMarkedException()));
    }

    public static TheoryData<Exception> TransientExceptions() =>
        new()
        {
            new TimeoutException(),
            new HttpRequestException(),
            new HttpRequestException("timeout", null, HttpStatusCode.RequestTimeout),
            new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests),
            new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable),
            new IOException(),
            new TransientTestDbException(),
            new MarkedTransientException()
        };

    public static TheoryData<Exception> PermanentExceptions() =>
        new()
        {
            new JsonException(),
            new UnauthorizedAccessException(),
            new ArgumentException(),
            new InvalidOperationException(),
            new HttpRequestException("bad request", null, HttpStatusCode.BadRequest),
            new PermanentTestDbException(),
            new MarkedPermanentException()
        };

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
            exception is ServiceSpecificException
                ? ConsumerExceptionDisposition.Transient
                : ConsumerExceptionDisposition.Unclassified;
    }

    private sealed class ServiceSpecificException : Exception;

    private sealed class TransientTestDbException : DbException
    {
        public override bool IsTransient => true;
    }

    private sealed class PermanentTestDbException : DbException
    {
        public override bool IsTransient => false;
    }

    private sealed class MarkedTransientException : Exception, ITransientConsumerFailure;

    private sealed class MarkedPermanentException : Exception, IPermanentConsumerFailure;

    private sealed class ConflictingMarkedException : Exception,
        ITransientConsumerFailure,
        IPermanentConsumerFailure;
}
