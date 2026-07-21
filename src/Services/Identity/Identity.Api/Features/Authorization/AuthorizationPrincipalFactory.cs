using System.Security.Claims;
using Identity.Api.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

internal static class AuthorizationPrincipalFactory
{
    internal const string SecurityStampClaim = "identity_security_stamp";

    public static async Task<ClaimsPrincipal> CreateUserPrincipalAsync(
        ApplicationUser user,
        IEnumerable<string> scopes,
        DateTimeOffset authenticationTime,
        UserManager<ApplicationUser> userManager,
        IOpenIddictScopeManager scopeManager)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString("D"))
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.EmailVerified, user.EmailConfirmed)
            .SetClaim(Claims.AuthenticationTime, authenticationTime.ToUnixTimeSeconds())
            .SetClaim(
                SecurityStampClaim,
                await userManager.GetSecurityStampAsync(user));

        var requestedScopes = scopes.ToHashSet(StringComparer.Ordinal);
        if (requestedScopes.Contains(Scopes.Roles))
        {
            identity.SetClaims(
                Claims.Role,
                [.. await userManager.GetRolesAsync(user)]);
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(requestedScopes);
        principal.SetResources(
            await scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());
        principal.SetDestinations(GetDestinations);
        return principal;
    }

    public static IEnumerable<string> GetDestinations(Claim claim)
    {
        if (claim.Type.StartsWith(Claims.Prefixes.Private, StringComparison.Ordinal))
        {
            yield break;
        }

        switch (claim.Type)
        {
            case Claims.Name or Claims.PreferredUsername:
                if (claim.Subject!.HasScope(Scopes.Profile))
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;
            case Claims.Email or Claims.EmailVerified:
                if (claim.Subject!.HasScope(Scopes.Email))
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;
            case Claims.Role:
                if (claim.Subject!.HasScope(Scopes.Roles))
                {
                    yield return Destinations.AccessToken;
                    yield return Destinations.IdentityToken;
                }

                yield break;
            case Claims.AuthenticationTime:
                yield return Destinations.IdentityToken;
                yield break;
            case SecurityStampClaim:
                yield break;
            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
