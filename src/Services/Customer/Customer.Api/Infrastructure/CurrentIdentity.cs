using System.Security.Claims;

namespace Customer.Api.Infrastructure;

internal sealed record CurrentIdentity(
    string Provider,
    string Subject,
    string? GivenName,
    string? FamilyName,
    string? Email)
{
    public static CurrentIdentity From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new UnauthorizedAccessException("The access token does not contain a subject.");
        }

        return new CurrentIdentity(
            "keycloak",
            subject,
            principal.FindFirstValue("given_name"),
            principal.FindFirstValue("family_name"),
            principal.FindFirstValue("email"));
    }
}
