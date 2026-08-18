namespace Payment.Api.Features.PaymentMethods.Common;

internal sealed record PaymentProblemDescriptor(string Code, string Title, ErrorCategory Category, string Description, bool Retryable)
{
    public string Type => $"/errors/v1/payment/{Code}";
    public int Status => Category switch { ErrorCategory.InvalidInput => 400, ErrorCategory.MissingResource => 404, ErrorCategory.StateConflict => 409, ErrorCategory.ConcurrencyConflict => 409, ErrorCategory.AuthenticationRequired => 401, ErrorCategory.AuthorizationDenied => 403, ErrorCategory.PreconditionRequired => 428, ErrorCategory.Unexpected => 503, _ => throw new ArgumentOutOfRangeException(nameof(Category), Category, "Unknown error category.") };
}
