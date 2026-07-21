using System.Security.Claims;

namespace Microservices.Security;

public static class SecurityClaimTypes
{
    public const string Subject = "sub";
    public const string ClientId = "client_id";
    public const string AuthorizedParty = "azp";
    public const string TenantId = "tenant_id";
    public const string TenantIdShort = "tid";
    public const string Scope = "scope";
    public const string Name = "name";
    public const string Role = "role";
}

public static class SecurityClaimsPrincipalExtensions
{
    public static string? GetSubject(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return GetUnambiguousValue(principal, SecurityClaimTypes.Subject);
    }

    public static string? GetClientId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return GetUnambiguousValue(
            principal,
            SecurityClaimTypes.ClientId,
            SecurityClaimTypes.AuthorizedParty);
    }

    public static string? GetTenantId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return GetUnambiguousValue(
            principal,
            SecurityClaimTypes.TenantId,
            SecurityClaimTypes.TenantIdShort);
    }

    private static string? GetUnambiguousValue(
        ClaimsPrincipal principal,
        params string[] claimTypes)
    {
        string? result = null;
        var found = false;

        foreach (var claim in principal.Identities
                     .Where(identity => identity.IsAuthenticated)
                     .SelectMany(identity => identity.Claims)
                     .Where(claim =>
                         claimTypes.Contains(claim.Type, StringComparer.Ordinal)))
        {
            if (string.IsNullOrWhiteSpace(claim.Value))
            {
                return null;
            }

            if (!found)
            {
                result = claim.Value;
                found = true;
                continue;
            }

            if (!string.Equals(result, claim.Value, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return result;
    }
}
