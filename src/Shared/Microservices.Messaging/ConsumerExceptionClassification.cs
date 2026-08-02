using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using System.Text.Json;

namespace Microservices.Messaging;

/// <summary>Marks a failure as transient only when the operation is safe and idempotent to retry.</summary>
public interface ITransientConsumerFailure;

/// <summary>Marks a failure as permanent and immediately faultable.</summary>
public interface IPermanentConsumerFailure;

/// <summary>
/// Marks a failure where the remote side may have completed the operation. It is not retryable by
/// itself; a dependency-specific rule may classify it as transient only when idempotency is proven.
/// </summary>
public interface IOutcomeUnknownConsumerFailure;

/// <summary>
/// Extension point for dependency-specific exception classification. Rules must use stable provider
/// data such as SQLSTATE, HTTP status, socket error, or broker error codes.
/// </summary>
public interface IConsumerExceptionRule
{
    ConsumerExceptionDisposition Classify(Exception exception);
}

public enum ConsumerExceptionDisposition
{
    Unclassified = 0,
    Transient = 1,
    Permanent = 2,
    Cancelled = 3
}

public interface IConsumerExceptionClassifier
{
    ConsumerExceptionDisposition Classify(Exception exception);

    bool IsTransient(Exception exception);
}

internal sealed class ConsumerExceptionClassifier(
    IEnumerable<IConsumerExceptionRule> rules) : IConsumerExceptionClassifier
{
    private readonly IConsumerExceptionRule[] _rules = rules.ToArray();

    public bool IsTransient(Exception exception) =>
        Classify(exception) == ConsumerExceptionDisposition.Transient;

    public ConsumerExceptionDisposition Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sawTransient = false;
        foreach (var current in Enumerate(exception))
        {
            var disposition = ClassifySingle(current);
            switch (disposition)
            {
                case ConsumerExceptionDisposition.Cancelled:
                case ConsumerExceptionDisposition.Permanent:
                    return disposition;
                case ConsumerExceptionDisposition.Transient:
                    sawTransient = true;
                    break;
                case ConsumerExceptionDisposition.Unclassified:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported consumer exception disposition '{disposition}'.");
            }
        }

        return sawTransient
            ? ConsumerExceptionDisposition.Transient
            : ConsumerExceptionDisposition.Permanent;
    }

    private ConsumerExceptionDisposition ClassifySingle(Exception exception)
    {
        foreach (var rule in _rules)
        {
            var disposition = rule.Classify(exception);
            if (disposition != ConsumerExceptionDisposition.Unclassified)
            {
                return disposition;
            }
        }

        if (exception is OperationCanceledException)
        {
            return ConsumerExceptionDisposition.Cancelled;
        }

        if (exception is IPermanentConsumerFailure)
        {
            return ConsumerExceptionDisposition.Permanent;
        }

        if (exception is IOutcomeUnknownConsumerFailure)
        {
            return ConsumerExceptionDisposition.Permanent;
        }

        if (exception is ITransientConsumerFailure)
        {
            return ConsumerExceptionDisposition.Transient;
        }

        return exception switch
        {
            DbException databaseException => ClassifyDatabase(databaseException),
            HttpRequestException httpException => ClassifyHttp(httpException),
            SocketException socketException => ClassifySocket(socketException),

            JsonException => ConsumerExceptionDisposition.Permanent,
            UnauthorizedAccessException => ConsumerExceptionDisposition.Permanent,
            SecurityException => ConsumerExceptionDisposition.Permanent,
            AuthenticationException => ConsumerExceptionDisposition.Permanent,
            ArgumentException => ConsumerExceptionDisposition.Permanent,
            FormatException => ConsumerExceptionDisposition.Permanent,
            InvalidCastException => ConsumerExceptionDisposition.Permanent,
            InvalidDataException => ConsumerExceptionDisposition.Permanent,
            NotSupportedException => ConsumerExceptionDisposition.Permanent,
            UriFormatException => ConsumerExceptionDisposition.Permanent,

            // TimeoutException and IOException are intentionally not broad retry categories.
            // A dependency-specific rule or a classified inner SocketException must prove transience.
            TimeoutException => ConsumerExceptionDisposition.Unclassified,
            IOException => ConsumerExceptionDisposition.Unclassified,
            _ => ConsumerExceptionDisposition.Unclassified
        };
    }

    private static ConsumerExceptionDisposition ClassifyDatabase(DbException exception)
    {
        var sqlState = exception.SqlState;
        if (string.IsNullOrWhiteSpace(sqlState))
        {
            return ConsumerExceptionDisposition.Unclassified;
        }

        if (sqlState.StartsWith("08", StringComparison.Ordinal) ||
            sqlState.StartsWith("40", StringComparison.Ordinal) ||
            sqlState is "53300" or "55P03" or "57P01" or "57P02" or "57P03")
        {
            return ConsumerExceptionDisposition.Transient;
        }

        // PostgreSQL authentication, authorization, integrity, syntax, schema, and configuration
        // errors are deterministic until code or configuration changes.
        if (sqlState.StartsWith("22", StringComparison.Ordinal) ||
            sqlState.StartsWith("23", StringComparison.Ordinal) ||
            sqlState.StartsWith("28", StringComparison.Ordinal) ||
            sqlState.StartsWith("2F", StringComparison.Ordinal) ||
            sqlState.StartsWith("3D", StringComparison.Ordinal) ||
            sqlState.StartsWith("42", StringComparison.Ordinal))
        {
            return ConsumerExceptionDisposition.Permanent;
        }

        return ConsumerExceptionDisposition.Unclassified;
    }

    private static ConsumerExceptionDisposition ClassifyHttp(HttpRequestException exception)
    {
        if (exception.StatusCode is null)
        {
            return ConsumerExceptionDisposition.Unclassified;
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout => ConsumerExceptionDisposition.Transient,

            HttpStatusCode.BadRequest or
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden or
            HttpStatusCode.NotFound or
            HttpStatusCode.MethodNotAllowed or
            HttpStatusCode.NotAcceptable or
            HttpStatusCode.Conflict or
            HttpStatusCode.Gone or
            HttpStatusCode.PreconditionFailed or
            HttpStatusCode.UnprocessableEntity => ConsumerExceptionDisposition.Permanent,

            // A generic 500 can represent a deterministic server-side defect or an operation with
            // unknown outcome. The shared classifier therefore requires a dependency-specific rule.
            _ => ConsumerExceptionDisposition.Unclassified
        };
    }

    private static ConsumerExceptionDisposition ClassifySocket(SocketException exception) =>
        exception.SocketErrorCode switch
        {
            SocketError.TimedOut or
            SocketError.ConnectionAborted or
            SocketError.ConnectionReset or
            SocketError.ConnectionRefused or
            SocketError.HostDown or
            SocketError.HostUnreachable or
            SocketError.NetworkDown or
            SocketError.NetworkReset or
            SocketError.NetworkUnreachable or
            SocketError.TryAgain or
            SocketError.WouldBlock => ConsumerExceptionDisposition.Transient,

            SocketError.AccessDenied or
            SocketError.AddressAlreadyInUse or
            SocketError.AddressNotAvailable or
            SocketError.HostNotFound or
            SocketError.InvalidArgument or
            SocketError.NoData or
            SocketError.ProtocolNotSupported or
            SocketError.SocketNotSupported => ConsumerExceptionDisposition.Permanent,

            _ => ConsumerExceptionDisposition.Unclassified
        };

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }
}
