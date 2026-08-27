using System.Security.Claims;
using Order.Api.Features.Orders.Common;

namespace Order.Api.Infrastructure;

internal sealed record CurrentOrderIdentity(string Provider, string Subject)
{
    private const string IdentityProvider = "keycloak";
    private const int MaximumSubjectLength = 255;

    public static Result<CurrentOrderIdentity> From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            return OrderApplicationErrors.AuthenticationRequired;
        }

        if (subject.Length > MaximumSubjectLength || !string.Equals(subject, subject.Trim(), StringComparison.Ordinal))
        {
            return OrderApplicationErrors.InvalidIdentityClaims;
        }

        return Result.Success(new CurrentOrderIdentity(IdentityProvider, subject));
    }
}
