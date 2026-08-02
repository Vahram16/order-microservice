using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microservices.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.Messaging.Tests;

public sealed class ConsumerExceptionClassifierTests
{
    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void SupportedTransientFailuresAreRetryable(Exception exception)
    {
        var classifier = CreateClassifier();

        Assert.Equal(ConsumerExceptionDisposition.Transient, classifier.Classify(exception));
        Assert.True(classifier.IsTransient(exception));
    }

    [Theory]
    [MemberData(nameof(PermanentExceptions))]
    public void PermanentAndUnknownFailuresAreNotRetryable(Exception exception)
    {
        var classifier = CreateClassifier();

        Assert.Equal(ConsumerExceptionDisposition.Permanent, classifier.Classify(exception));
        Assert.False(classifier.IsTransient(exception));
    }

    [Fact]
    public void CancellationIsSeparateFromFailureAndIsNeverRetried()
    {
        var classifier = CreateClassifier();
        var exception = new OperationCanceledException();

        Assert.Equal(ConsumerExceptionDisposition.Cancelled, classifier.Classify(exception));
        Assert.False(classifier.IsTransient(exception));
    }

    [Fact]
    public void ServiceRuleClassifiesStableDependencyCode()
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
    public void WrappedProviderFailureIsClassifiedFromStableInnerException()
    {
        var classifier = CreateClassifier();
        var exception = new InvalidOperationException(
            "provider wrapper",
            new TestDbException("40001"));

        Assert.Equal(ConsumerExceptionDisposition.Transient, classifier.Classify(exception));
    }

    [Fact]
    public void PermanentSqlStateOverridesProviderTransientFlag()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerExceptionDisposition.Permanent,
            classifier.Classify(new TestDbException("23505", isTransient: true)));
    }

    [Fact]
    public void PermanentFailureInAggregateTakesPrecedenceOverTransientFailure()
    {
        var classifier = CreateClassifier();
        var exception = new AggregateException(
            new SocketException((int)SocketError.ConnectionReset),
            new ArgumentException("invalid message"));

        Assert.Equal(ConsumerExceptionDisposition.Permanent, classifier.Classify(exception));
    }

    [Fact]
    public void PermanentMarkerTakesPrecedenceOverTransientMarker()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerExceptionDisposition.Permanent,
            classifier.Classify(new ConflictingMarkedException()));
    }

    public static TheoryData<Exception> TransientExceptions() =>
        new()
        {
            new HttpRequestException("timeout", null, HttpStatusCode.RequestTimeout),
            new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests),
            new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway),
            new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable),
            new HttpRequestException("gateway timeout", null, HttpStatusCode.GatewayTimeout),
            new SocketException((int)SocketError.ConnectionReset),
            new IOException("socket wrapper", new SocketException((int)SocketError.NetworkUnreachable)),
            new TestDbException("08006"),
            new TestDbException("40001"),
            new TestDbException("40P01"),
            new TestDbException("53300"),
            new TestDbException("55P03"),
            new TestDbException("57P01"),
            new TestDbException(null, isTransient: true),
            new MarkedTransientException()
        };

    public static TheoryData<Exception> PermanentExceptions() =>
        new()
        {
            new TimeoutException(),
            new HttpRequestException(),
            new HttpRequestException("server defect", null, HttpStatusCode.InternalServerError),
            new HttpRequestException("bad request", null, HttpStatusCode.BadRequest),
            new JsonException(),
            new UnauthorizedAccessException(),
            new ArgumentException(),
            new InvalidOperationException(),
            new IOException(),
            new SocketException((int)SocketError.HostNotFound),
            new TestDbException("23505"),
            new TestDbException("28P01"),
            new TestDbException("42P01"),
            new TestDbException(null),
            new MarkedPermanentException(),
            new OutcomeUnknownException()
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

    private sealed class TestDbException(
        string? sqlState,
        bool isTransient = false) : DbException
    {
        public override string? SqlState => sqlState;

        public override bool IsTransient => isTransient;
    }

    private sealed class MarkedTransientException : Exception, ITransientConsumerFailure;

    private sealed class MarkedPermanentException : Exception, IPermanentConsumerFailure;

    private sealed class OutcomeUnknownException : Exception, IOutcomeUnknownConsumerFailure;

    private sealed class ConflictingMarkedException : Exception,
        ITransientConsumerFailure,
        IPermanentConsumerFailure;
}
