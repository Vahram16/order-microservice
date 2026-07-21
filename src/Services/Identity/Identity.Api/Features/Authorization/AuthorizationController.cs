using System.Security.Claims;
using System.Text.Encodings.Web;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AuthorizationController(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictScopeManager scopeManager,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAntiforgery antiforgery,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
    : Controller
{
    private const string SecurityStampClaim = "identity_security_stamp";

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        var authentication = await HttpContext.AuthenticateAsync(
            IdentityConstants.ApplicationScheme);
        var authorizationReturnUri = Request.PathBase + Request.Path +
            QueryString.Create(Request.HasFormContentType
                ? Request.Form
                : Request.Query);
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
                HttpContext,
                dataProtectionProvider,
                authorizationReturnUri);

        if (authentication is not { Succeeded: true } ||
            forceAuthentication && !reauthenticationMarkerMatched)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return ProtocolForbid(Errors.LoginRequired, "The user is not signed in.");
            }

            if (forceAuthentication)
            {
                OidcAuthenticationState.IssueReauthenticationMarker(
                    HttpContext,
                    dataProtectionProvider,
                    authorizationReturnUri,
                    timeProvider.GetUtcNow());
            }

            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = authorizationReturnUri
                },
                IdentityConstants.ApplicationScheme);
        }

        var user = await userManager.GetUserAsync(authentication.Principal);
        if (user is null || !user.IsActive || !await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignOutAsync();
            if (request.HasPromptValue(PromptValues.None))
            {
                return ProtocolForbid(Errors.LoginRequired, "The user is no longer allowed to sign in.");
            }

            if (forceAuthentication)
            {
                OidcAuthenticationState.IssueReauthenticationMarker(
                    HttpContext,
                    dataProtectionProvider,
                    authorizationReturnUri,
                    timeProvider.GetUtcNow());
            }

            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = authorizationReturnUri
                },
                IdentityConstants.ApplicationScheme);
        }

        var application = await applicationManager.FindByClientIdAsync(request.ClientId!) ??
            throw new InvalidOperationException("The client application cannot be found.");
        if (!await applicationManager.HasConsentTypeAsync(application, ConsentTypes.Implicit))
        {
            return ProtocolForbid(
                Errors.ConsentRequired,
                "This server only provisions first-party clients with pre-established consent.");
        }

        var subject = await userManager.GetUserIdAsync(user);
        var applicationId = await applicationManager.GetIdAsync(application) ??
            throw new InvalidOperationException("The client application identifier cannot be resolved.");
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
            authenticationTime.Value);
        var authorization = authorizations.LastOrDefault() ??
            await authorizationManager.CreateAsync(
                principal,
                subject,
                applicationId,
                AuthorizationTypes.Permanent,
                principal.GetScopes());
        principal.SetAuthorizationId(await authorizationManager.GetIdAsync(authorization));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    [EnableRateLimiting(IdentityServiceExtensions.TokenRateLimitPolicy)]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
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
                await scopeManager.ListResourcesAsync(servicePrincipal.GetScopes()).ToListAsync());
            servicePrincipal.SetDestinations(GetDestinations);

            return SignIn(
                servicePrincipal,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The grant type is not supported.");
        }

        var authentication = await HttpContext.AuthenticateAsync(
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
            return ProtocolForbid(Errors.InvalidGrant, "The authorization grant is no longer valid.");
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

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> UserInfo()
    {
        var subject = User.GetClaim(Claims.Subject);
        var user = string.IsNullOrWhiteSpace(subject)
            ? null
            : await userManager.FindByIdAsync(subject);
        if (user is null || !user.IsActive || !await signInManager.CanSignInAsync(user))
        {
            return Unauthorized();
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString("D")
        };
        if (User.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName;
            claims[Claims.PreferredUsername] = user.UserName;
        }

        if (User.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (User.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await userManager.GetRolesAsync(user);
        }

        return Ok(claims);
    }

    [AllowAnonymous]
    [HttpGet("~/connect/logout")]
    public IActionResult Logout()
    {
        var token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken ??
            throw new InvalidOperationException("An antiforgery token could not be created.");
        var encoder = HtmlEncoder.Default;
        var action = encoder.Encode(Request.PathBase + Request.Path + Request.QueryString);
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
        return Content(content, "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpPost("~/connect/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutPost()
    {
        await signInManager.SignOutAsync();
        return SignOut(
            properties: new AuthenticationProperties { RedirectUri = "/" },
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<ClaimsPrincipal> CreateUserPrincipalAsync(
        ApplicationUser user,
        IEnumerable<string> scopes,
        DateTimeOffset authenticationTime)
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
            .SetClaim(SecurityStampClaim, await userManager.GetSecurityStampAsync(user));

        var requestedScopes = scopes.ToHashSet(StringComparer.Ordinal);
        if (requestedScopes.Contains(Scopes.Roles))
        {
            identity.SetClaims(Claims.Role, [.. await userManager.GetRolesAsync(user)]);
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(requestedScopes);
        principal.SetResources(
            await scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());
        principal.SetDestinations(GetDestinations);
        return principal;
    }

    private ForbidResult ProtocolForbid(string error, string description) =>
        Forbid(
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

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
