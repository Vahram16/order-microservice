using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;

namespace Microservices.Messaging;

/// <summary>Marks a failure as transient for message retry and redelivery.</summary>
public interface ITransientConsumerFailure;

/// <summary>Marks a failure as permanent and immediately faultable.</summary>
public interface IPermanentConsumerFailure;

/// <summary>
/// Extension point for service-specific exception classification. Rules are evaluated in
/// registration order before the shared defaults.
/// </summary>
public interface IConsumerExceptionRule
{
    ConsumerExceptionDisposition Classify(Exception exception);
}

public enum ConsumerExceptionDisposition
{
    Unclassified = 0,
    Transient = 1,
    Permanent = 2
}

public interface IConsumerExceptionClassifier
{
    bool IsTransient(Exception exception);
}

internal sealed class ConsumerExceptionClassifier(
    IEnumerable<IConsumerExceptionRule> rules) : IConsumerExceptionClassifier
{
    public bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var current = Unwrap(exception);
        foreach (var rule in rules)
        {
            var disposition = rule.Classify(current);
            if (disposition != ConsumerExceptionDisposition.Unclassified)
            {
                return disposition == ConsumerExceptionDisposition.Transient;
            }
        }

        if (current is IPermanentConsumerFailure)
        {
            return false;
        }

        if (current is ITransientConsumerFailure)
        {
            return true;
        }

        return current switch
        {
            TimeoutException => true,
            DbException databaseException => IsTransientDatabaseFailure(databaseException),
            HttpRequestException httpException => IsTransientHttpFailure(httpException),
            SocketException => true,
            IOException => true,

            JsonException => false,
            UnauthorizedAccessException => false,
            SecurityException => false,
            ArgumentException => false,
            NotSupportedException => false,
            _ => false
        };
    }

    private static bool IsTransientDatabaseFailure(DbException exception)
    {
        var isTransientProperty = exception.GetType().GetProperty("IsTransient");
        return isTransientProperty?.PropertyType == typeof(bool) &&
               isTransientProperty.GetValue(exception) is true;
    }

    private static bool IsTransientHttpFailure(HttpRequestException exception) =>
        exception.StatusCode is null or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            exception = aggregate.InnerExceptions[0];
        }

        return exception;
    }
}
