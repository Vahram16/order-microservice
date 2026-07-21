using System.Security.Claims;
using Identity.Api.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

internal static class TokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var request = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "The OpenID Connect request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            var application = await applicationManager.FindByClientIdAsync(request.ClientId!) ??
                throw new InvalidOperationException("The client application cannot be found.");
            var displayName = await applicationManager.GetLocalizedDisplayNameAsync(application) ??
                request.ClientId!;
            var identity = new ClaimsIdentity(
                TokenValidationParameters.DefaultAuthenticationType,
                Claims.Name,
                Claims.Role);
            identity.SetClaim(Claims.Subject, $"client:{request.ClientId}")
                .SetClaim(Claims.Name, displayName);

            var servicePrincipal = new ClaimsPrincipal(identity);
            servicePrincipal.SetScopes(request.GetScopes());
            servicePrincipal.SetResources(
                await scopeManager.ListResourcesAsync(
                    servicePrincipal.GetScopes()).ToListAsync());
            servicePrincipal.SetDestinations(
                AuthorizationPrincipalFactory.GetDestinations);

            return Results.SignIn(
                servicePrincipal,
                authenticationScheme:
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (!request.IsAuthorizationCodeGrantType() &&
            !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The grant type is not supported.");
        }

        var authentication = await context.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (authentication is not { Succeeded: true, Principal: { } storedPrincipal })
        {
            return AuthorizationProtocolResults.Forbid(
                Errors.InvalidGrant,
                "The authorization grant cannot be read.");
        }

        var subject = storedPrincipal.GetClaim(Claims.Subject);
        var user = string.IsNullOrWhiteSpace(subject)
            ? null
            : await userManager.FindByIdAsync(subject);

        if (user is null ||
            !user.IsActive ||
            !await signInManager.CanSignInAsync(user) ||
            await userManager.IsLockedOutAsync(user) ||
            !string.Equals(
                storedPrincipal.GetClaim(
                    AuthorizationPrincipalFactory.SecurityStampClaim),
                await userManager.GetSecurityStampAsync(user),
                StringComparison.Ordinal))
        {
            return AuthorizationProtocolResults.Forbid(
                Errors.InvalidGrant,
                "The authorization grant is no longer valid.");
        }

        var scopes = request.IsRefreshTokenGrantType() && request.GetScopes().Any()
            ? request.GetScopes()
            : storedPrincipal.GetScopes();
        var requestedScopes = scopes.ToHashSet(StringComparer.Ordinal);
        var refreshedIdentity = new ClaimsIdentity(
            storedPrincipal.Claims,
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        refreshedIdentity.SetClaim(Claims.Subject, user.Id.ToString("D"))
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.EmailVerified, user.EmailConfirmed)
            .SetClaim(
                AuthorizationPrincipalFactory.SecurityStampClaim,
                await userManager.GetSecurityStampAsync(user));
        refreshedIdentity.SetClaims(
            Claims.Role,
            requestedScopes.Contains(Scopes.Roles)
                ? [.. await userManager.GetRolesAsync(user)]
                : []);

        var principal = new ClaimsPrincipal(refreshedIdentity);
        principal.SetScopes(requestedScopes);
        principal.SetResources(
            await scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());
        principal.SetAuthorizationId(storedPrincipal.GetAuthorizationId());
        principal.SetDestinations(AuthorizationPrincipalFactory.GetDestinations);

        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
