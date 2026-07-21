using System.Security.Claims;
using System.Text.Encodings.Web;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

internal static class AuthorizationEndpoints
{
    private const string SecurityStampClaim = "identity_security_stamp";

    public static IEndpointRouteBuilder MapAuthorizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(
                "/connect/authorize",
                [HttpMethods.Get, HttpMethods.Post],
                AuthorizeAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapPost("/connect/token", ExchangeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.TokenRateLimitPolicy)
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .ExcludeFromDescription();

        endpoints.MapMethods(
                "/connect/userinfo",
                [HttpMethods.Get, HttpMethods.Post],
                UserInfoAsync)
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            })
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet("/connect/logout", RenderLogout)
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapPost("/connect/logout", LogoutAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
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
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
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
            authentication is { Succeeded: true } &&
            OidcAuthenticationState.ConsumeReauthenticationMarker(
                context,
                dataProtectionProvider,
                authorizationReturnUri);

        if (authentication is not { Succeeded: true } ||
            forceAuthentication && !reauthenticationMarkerMatched)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return ProtocolForbid(
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

        var user = await userManager.GetUserAsync(authentication.Principal);
        if (user is null || !user.IsActive || !await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignOutAsync();
            if (request.HasPromptValue(PromptValues.None))
            {
                return ProtocolForbid(
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
            return ProtocolForbid(
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
        var principal = await CreateUserPrincipalAsync(
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

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var request = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

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
            servicePrincipal.SetDestinations(GetDestinations);

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
            return ProtocolForbid(
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
                storedPrincipal.GetClaim(SecurityStampClaim),
                await userManager.GetSecurityStampAsync(user),
                StringComparison.Ordinal))
        {
            return ProtocolForbid(
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
                SecurityStampClaim,
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
        principal.SetDestinations(GetDestinations);

        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> UserInfoAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var subject = context.User.GetClaim(Claims.Subject);
        var user = string.IsNullOrWhiteSpace(subject)
            ? null
            : await userManager.FindByIdAsync(subject);
        if (user is null || !user.IsActive || !await signInManager.CanSignInAsync(user))
        {
            return Results.Unauthorized();
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString("D")
        };
        if (context.User.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName;
            claims[Claims.PreferredUsername] = user.UserName;
        }

        if (context.User.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (context.User.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await userManager.GetRolesAsync(user);
        }

        return Results.Ok(claims);
    }

    private static IResult RenderLogout(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException(
                "An antiforgery token could not be created.");
        var encoder = HtmlEncoder.Default;
        var action = encoder.Encode(
            context.Request.PathBase +
            context.Request.Path +
            context.Request.QueryString);
        var content = $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Sign out</title>
            </head>
            <body>
              <main>
                <h1>Sign out</h1>
                <p>Confirm that you want to end your identity session.</p>
                <form method="post" action="{action}">
                  <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
                  <button type="submit">Sign out</button>
                </form>
              </main>
            </body>
            </html>
            """;
        return Results.Content(content, "text/html; charset=utf-8");
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        await signInManager.SignOutAsync();
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<ClaimsPrincipal> CreateUserPrincipalAsync(
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

    private static IResult ProtocolForbid(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static IEnumerable<string> GetDestinations(Claim claim)
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
