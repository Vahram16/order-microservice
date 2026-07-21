using Identity.Api.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
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
            var principal = await AuthorizationPrincipalFactory.CreateServicePrincipalAsync(
                request.ClientId!,
                displayName,
                request.GetScopes(),
                scopeManager);

            return Results.SignIn(
                principal,
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
        var principal = await AuthorizationPrincipalFactory.RefreshUserPrincipalAsync(
            storedPrincipal,
            user,
            scopes,
            userManager,
            scopeManager);

        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
