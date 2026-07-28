using System.Security.Claims;

namespace Microservices.Security;

internal static class AccessTokenClaimsValidator
{
    private const string AuthorizedPartyClaim = "azp";

    public static bool TryValidate(
        ClaimsPrincipal principal,
        ApiSecurityOptions options,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(options);

        var authenticatedIdentities = principal.Identities
            .OfType<ClaimsIdentity>()
            .Where(identity => identity.IsAuthenticated)
            .ToArray();

        if (authenticatedIdentities.Length == 0)
        {
            failure = "The access token did not produce an authenticated identity.";
            return false;
        }

        foreach (var claimType in options.RequiredClaims)
        {
            if (!HasNonEmptyClaim(authenticatedIdentities, claimType))
            {
                failure = $"The access token is missing the required '{claimType}' claim.";
                return false;
            }
        }

        var authorizedParties = authenticatedIdentities
            .SelectMany(identity => identity.FindAll(AuthorizedPartyClaim))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (authorizedParties.Length != 1)
        {
            failure = "The access token must contain exactly one non-empty 'azp' claim.";
            return false;
        }

        if (!options.ValidAuthorizedParties.Contains(
                authorizedParties[0],
                StringComparer.Ordinal))
        {
            failure = "The access token was issued to an unauthorized client.";
            return false;
        }

        failure = null;
        return true;
    }

    private static bool HasNonEmptyClaim(
        IEnumerable<ClaimsIdentity> identities,
        string claimType) =>
        identities
            .SelectMany(identity => identity.FindAll(claimType))
            .Any(claim => !string.IsNullOrWhiteSpace(claim.Value));
}
