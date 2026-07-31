using System.Data.Common;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;

namespace Microservices.Messaging;

/// <summary>Marks an exception as transient for message retry and redelivery.</summary>
public interface ITransientMessageException;

/// <summary>Marks an exception as permanent and immediately faultable.</summary>
public interface IPermanentMessageException;

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

        if (current is IPermanentMessageException)
        {
            return false;
        }

        if (current is ITransientMessageException)
        {
            return true;
        }

        return current switch
        {
            TimeoutException => true,
            DbException => true,
            HttpRequestException => true,
            IOException => true,
            SocketException => true,

            JsonException => false,
            UnauthorizedAccessException => false,
            SecurityException => false,
            ArgumentException => false,
            NotSupportedException => false,
            _ => false
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            exception = aggregate.InnerExceptions[0];
        }

        return exception;
    }
}
