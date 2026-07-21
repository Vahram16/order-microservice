using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Validation.AspNetCore;

namespace Identity.Api.Features.Authorization;

internal static class AuthorizationEndpointExtensions
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(
                "/connect/authorize",
                [HttpMethods.Get, HttpMethods.Post],
                AuthorizeEndpoint.HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapPost("/connect/token", TokenEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.TokenRateLimitPolicy)
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .ExcludeFromDescription();

        endpoints.MapMethods(
                "/connect/userinfo",
                [HttpMethods.Get, HttpMethods.Post],
                UserInfoEndpoint.HandleAsync)
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            })
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost("/connect/logout", LogoutEndpoint.Handle)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }
}
