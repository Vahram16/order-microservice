using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

internal static class AuthorizeEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        var request = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "The OpenID Connect request cannot be retrieved.");
        var authentication = await context.AuthenticateAsync(
            IdentityConstants.ApplicationScheme);
        var authorizationReturnUri = context.Request.PathBase + context.Request.Path +
            QueryString.Create(context.Request.HasFormContentType
                ? context.Request.Form
                : context.Request.Query);
        var authenticationTime = OidcAuthenticationState.GetAuthenticationTime(
            authentication.Properties);
        var forceAuthentication =
            request.HasPromptValue(PromptValues.Login) ||
            request.MaxAge is 0 ||
            (request.MaxAge is not null &&
             (authenticationTime is null ||
              timeProvider.GetUtcNow() - authenticationTime.Value >
              TimeSpan.FromSeconds(request.MaxAge.Value)));
        var reauthenticationMarkerMatched =
            authentication is { Succeeded: true, Principal: not null } &&
            OidcAuthenticationState.ConsumeReauthenticationMarker(
                context,
                dataProtectionProvider,
                authorizationReturnUri);
        var authenticatedPrincipal = authentication.Principal;

        if (!authentication.Succeeded ||
            authenticatedPrincipal is null ||
            forceAuthentication && !reauthenticationMarkerMatched)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return AuthorizationProtocolResults.Forbid(
                    Errors.LoginRequired,
                    "The user is not signed in.");
            }

            if (forceAuthentication)
            {
                OidcAuthenticationState.IssueReauthenticationMarker(
                    context,
                    dataProtectionProvider,
                    authorizationReturnUri,
                    timeProvider.GetUtcNow());
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = authorizationReturnUri },
                [IdentityConstants.ApplicationScheme]);
        }

        var user = await userManager.GetUserAsync(authenticatedPrincipal);
        if (user is null || !user.IsActive || !await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignOutAsync();
            if (request.HasPromptValue(PromptValues.None))
            {
                return AuthorizationProtocolResults.Forbid(
                    Errors.LoginRequired,
                    "The user is no longer allowed to sign in.");
            }

            if (forceAuthentication)
            {
                OidcAuthenticationState.IssueReauthenticationMarker(
                    context,
                    dataProtectionProvider,
                    authorizationReturnUri,
                    timeProvider.GetUtcNow());
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = authorizationReturnUri },
                [IdentityConstants.ApplicationScheme]);
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The client application cannot be found.");
        if (!await applicationManager.HasConsentTypeAsync(
                application,
                ConsentTypes.Implicit))
        {
            return AuthorizationProtocolResults.Forbid(
                Errors.ConsentRequired,
                "This server only provisions first-party clients with pre-established consent.");
        }

        var subject = await userManager.GetUserIdAsync(user);
        var applicationId = await applicationManager.GetIdAsync(application) ??
            throw new InvalidOperationException(
                "The client application identifier cannot be resolved.");
        var authorizations = await authorizationManager.FindAsync(
            subject: subject,
            client: applicationId,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()).ToListAsync();

        authenticationTime ??= authentication.Properties?.IssuedUtc ??
            timeProvider.GetUtcNow();
        var principal = await AuthorizationPrincipalFactory.CreateUserPrincipalAsync(
            user,
            request.GetScopes(),
            authenticationTime.Value,
            userManager,
            scopeManager);
        var authorization = authorizations.LastOrDefault() ??
            await authorizationManager.CreateAsync(
                principal,
                subject,
                applicationId,
                AuthorizationTypes.Permanent,
                principal.GetScopes());
        principal.SetAuthorizationId(
            await authorizationManager.GetIdAsync(authorization));

        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
