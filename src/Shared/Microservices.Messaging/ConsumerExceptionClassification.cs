namespace Microservices.Messaging;

/// <summary>Marks a failure as transient when the owning operation is safe to retry.</summary>
public interface ITransientConsumerFailure;

/// <summary>Marks a failure as permanent and immediately faultable.</summary>
public interface IPermanentConsumerFailure;

/// <summary>
/// Marks a failure whose outcome is unknown. It is permanent unless a dependency-specific rule can
/// prove that replay is safe and idempotent.
/// </summary>
public interface IOutcomeUnknownConsumerFailure;

/// <summary>
/// Extension point for service-owned dependency rules. Shared messaging infrastructure deliberately
/// does not guess retry safety from broad HTTP, socket, timeout, or database exception categories.
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
        if (exception is OperationCanceledException)
        {
            return ConsumerExceptionDisposition.Cancelled;
        }

        if (exception is IPermanentConsumerFailure or IOutcomeUnknownConsumerFailure)
        {
            return ConsumerExceptionDisposition.Permanent;
        }

        foreach (var rule in _rules)
        {
            var disposition = rule.Classify(exception);
            if (disposition != ConsumerExceptionDisposition.Unclassified)
            {
                return disposition;
            }
        }

        return exception is ITransientConsumerFailure
            ? ConsumerExceptionDisposition.Transient
            : ConsumerExceptionDisposition.Unclassified;
    }

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
