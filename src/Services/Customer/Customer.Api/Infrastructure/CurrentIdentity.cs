using System.Net.Mail;
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
    private const int MaximumNameLength = 100;
    private const int MaximumEmailLength = 320;

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

        if (!TryNormalizeOptionalClaim(
                principal.FindFirstValue("given_name"),
                MaximumNameLength,
                out var givenName) ||
            !TryNormalizeOptionalClaim(
                principal.FindFirstValue("family_name"),
                MaximumNameLength,
                out var familyName))
        {
            return CustomerApplicationErrors.InvalidIdentityClaims;
        }

        var emailVerified = string.Equals(
            principal.FindFirstValue("email_verified"),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);
        string? email = null;
        if (emailVerified &&
            (!TryNormalizeOptionalClaim(
                    principal.FindFirstValue("email"),
                    MaximumEmailLength,
                    out email) ||
             !IsValidEmail(email)))
        {
            return CustomerApplicationErrors.InvalidIdentityClaims;
        }

        return Result.Success(new CurrentIdentity(
            KeycloakProvider,
            subject,
            givenName,
            familyName,
            email?.ToLowerInvariant()));
    }

    private static bool TryNormalizeOptionalClaim(
        string? value,
        int maximumLength,
        out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= maximumLength;
    }

    private static bool IsValidEmail(string? value) =>
        value is null ||
        MailAddress.TryCreate(value, out var address) &&
        string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
}
