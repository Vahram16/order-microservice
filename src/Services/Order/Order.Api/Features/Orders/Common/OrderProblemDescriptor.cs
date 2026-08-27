namespace Order.Api.Features.Orders.Common;

internal sealed record OrderProblemDescriptor(
    string Code,
    string Title,
    ErrorCategory Category,
    string Description,
    bool Retryable)
{
    public string Type => $"/errors/v1/order/{Code}";

    public int Status => Category switch
    {
        ErrorCategory.InvalidInput => StatusCodes.Status400BadRequest,
        ErrorCategory.MissingResource => StatusCodes.Status404NotFound,
        ErrorCategory.StateConflict => StatusCodes.Status409Conflict,
        ErrorCategory.ConcurrencyConflict => StatusCodes.Status409Conflict,
        ErrorCategory.AuthenticationRequired => StatusCodes.Status401Unauthorized,
        ErrorCategory.AuthorizationDenied => StatusCodes.Status403Forbidden,
        ErrorCategory.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
        ErrorCategory.Unexpected => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(Category), Category, "Unknown error category.")
    };
}
