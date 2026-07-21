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
        var identity = CreateIdentity(user);
        identity.SetClaim(
            Claims.AuthenticationTime,
            authenticationTime.ToUnixTimeSeconds());
        await SetSecurityStampAsync(identity, user, userManager);

        var requestedScopes = scopes.ToHashSet(StringComparer.Ordinal);
        await SetRolesAsync(identity, user, requestedScopes, userManager);
        return await CreatePrincipalAsync(identity, requestedScopes, scopeManager);
    }

    public static async Task<ClaimsPrincipal> RefreshUserPrincipalAsync(
        ClaimsPrincipal storedPrincipal,
        ApplicationUser user,
        IEnumerable<string> scopes,
        UserManager<ApplicationUser> userManager,
        IOpenIddictScopeManager scopeManager)
    {
        var identity = new ClaimsIdentity(
            storedPrincipal.Claims,
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        SetUserClaims(identity, user);
        await SetSecurityStampAsync(identity, user, userManager);

        var requestedScopes = scopes.ToHashSet(StringComparer.Ordinal);
        await SetRolesAsync(identity, user, requestedScopes, userManager);
        var principal = await CreatePrincipalAsync(
            identity,
            requestedScopes,
            scopeManager);
        principal.SetAuthorizationId(storedPrincipal.GetAuthorizationId());
        return principal;
    }

    public static async Task<ClaimsPrincipal> CreateServicePrincipalAsync(
        string clientId,
        string displayName,
        IEnumerable<string> scopes,
        IOpenIddictScopeManager scopeManager)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, $"client:{clientId}")
            .SetClaim(Claims.Name, displayName);

        return await CreatePrincipalAsync(
            identity,
            scopes.ToHashSet(StringComparer.Ordinal),
            scopeManager);
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

    private static ClaimsIdentity CreateIdentity(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        SetUserClaims(identity, user);
        return identity;
    }

    private static void SetUserClaims(
        ClaimsIdentity identity,
        ApplicationUser user) =>
        identity.SetClaim(Claims.Subject, user.Id.ToString("D"))
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.EmailVerified, user.EmailConfirmed);

    private static async Task SetSecurityStampAsync(
        ClaimsIdentity identity,
        ApplicationUser user,
        UserManager<ApplicationUser> userManager) =>
        identity.SetClaim(
            SecurityStampClaim,
            await userManager.GetSecurityStampAsync(user));

    private static async Task SetRolesAsync(
        ClaimsIdentity identity,
        ApplicationUser user,
        IReadOnlySet<string> scopes,
        UserManager<ApplicationUser> userManager)
    {
        IEnumerable<string> roles = scopes.Contains(Scopes.Roles)
            ? await userManager.GetRolesAsync(user)
            : Array.Empty<string>();
        identity.SetClaims(Claims.Role, roles);
    }

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        ClaimsIdentity identity,
        IReadOnlySet<string> scopes,
        IOpenIddictScopeManager scopeManager)
    {
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetResources(
            await scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());
        principal.SetDestinations(GetDestinations);
        return principal;
    }
}
