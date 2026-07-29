using System.Security.Claims;

namespace Customer.Api.Infrastructure;

internal sealed record CurrentIdentity(
    string Provider,
    string Subject,
    string? GivenName,
    string? FamilyName,
    string? Email)
{
    private const string KeycloakProvider = "keycloak";

    public static CurrentIdentity From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new UnauthorizedAccessException("The access token does not contain a subject.");
        }

        var emailVerified = string.Equals(
            principal.FindFirstValue("email_verified"),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

        return new CurrentIdentity(
            KeycloakProvider,
            subject,
            principal.FindFirstValue("given_name"),
            principal.FindFirstValue("family_name"),
            emailVerified ? principal.FindFirstValue("email") : null);
    }
}
