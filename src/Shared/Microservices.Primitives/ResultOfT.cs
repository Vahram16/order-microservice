namespace Microservices.Primitives;

public sealed class Result<TValue>
{
    private readonly TValue? _value;
    private readonly OperationError? _error;

    internal Result(TValue value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value), "A successful result cannot contain a null value.");
        }

        _value = value;
        _error = null;
        IsSuccess = true;
    }

    internal Result(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("A failed result does not contain a value.");

    public OperationError Error =>
        IsFailure
            ? _error!
            : throw new InvalidOperationException("A successful result does not contain an error.");

    public Result<TNext> Map<TNext>(Func<TValue, TNext> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        return IsSuccess
            ? Result.Success(mapper(Value))
            : Result.Failure<TNext>(Error);
    }

    public Result<TNext> Bind<TNext>(Func<TValue, Result<TNext>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        if (IsFailure)
        {
            return Result.Failure<TNext>(Error);
        }

        return binder(Value)
            ?? throw new InvalidOperationException("A result binder cannot return null.");
    }

    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<OperationError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(Value) : onFailure(Error);
    }

    public static implicit operator Result<TValue>(OperationError error) => new(error);
}
