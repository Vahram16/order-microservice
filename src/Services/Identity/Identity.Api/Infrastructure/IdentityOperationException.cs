using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Infrastructure;

public sealed class IdentityOperationException : Exception
{
    public IdentityOperationException(IEnumerable<IdentityError> errors)
        : base("The identity operation could not be completed.")
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<IdentityError> Errors { get; }
}

public sealed class InvalidAccountTokenException()
    : Exception("The account token is invalid or has expired.");
