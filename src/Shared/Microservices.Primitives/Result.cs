namespace Microservices.Primitives;

public sealed class Result
{
    private readonly OperationError? _error;

    private Result(bool isSuccess, OperationError? error)
    {
        if (isSuccess == (error is not null))
        {
            throw new ArgumentException("A successful result cannot contain an error and a failed result must contain one.");
        }

        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public OperationError Error =>
        IsFailure
            ? _error!
            : throw new InvalidOperationException("A successful result does not contain an error.");

    public static Result Success() => new(true, null);

    public static Result Failure(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }

    public static Result<TValue> Success<TValue>(TValue value) => new(value);

    public static Result<TValue> Failure<TValue>(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(error);
    }

    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<OperationError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    public static implicit operator Result(OperationError error) => Failure(error);
}
