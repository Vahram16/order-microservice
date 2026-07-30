using System.Security.Claims;
using Customer.Api.Features.Customers.Common;

namespace Customer.Api.Infrastructure;

internal sealed record CurrentIdentity(
    string Provider,
    string Subject,
    string? GivenName,
    string? FamilyName,
    string? Email)
{
    private const string KeycloakProvider = "keycloak";
    private const int MaximumSubjectLength = 255;

    public static Result<CurrentIdentity> From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            return CustomerApplicationErrors.AuthenticationRequired;
        }

        if (subject.Length > MaximumSubjectLength ||
            !string.Equals(subject, subject.Trim(), StringComparison.Ordinal))
        {
            return CustomerApplicationErrors.InvalidIdentityClaims;
        }

        var emailVerified = string.Equals(
            principal.FindFirstValue("email_verified"),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

        return Result.Success(new CurrentIdentity(
            KeycloakProvider,
            subject,
            principal.FindFirstValue("given_name"),
            principal.FindFirstValue("family_name"),
            emailVerified ? principal.FindFirstValue("email") : null));
    }
}
