using Identity.Api.Features.Sessions.SigningIn.Authenticator.V1;
using Identity.Api.Features.Sessions.SigningIn.Password.V1;
using Identity.Api.Features.Sessions.SigningIn.RecoveryCode.V1;
using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Sessions.SigningIn;

internal static class SignInEndpointExtensions
{
    public static IEndpointRouteBuilder MapSignIn(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/sessions",
                PasswordLoginEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<LoginResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .WithName("CreateIdentitySession")
            .WithSummary("Create an Identity browser session using a password.");

        endpoints.MapPost(
                "/api/v1/sessions/two-factor/authenticator",
                AuthenticatorCodeEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .WithName("CompleteIdentityAuthenticatorSignIn")
            .WithSummary("Complete an Identity session using an authenticator code.");

        endpoints.MapPost(
                "/api/v1/sessions/two-factor/recovery-code",
                RecoveryCodeEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .WithName("CompleteIdentityRecoveryCodeSignIn")
            .WithSummary("Complete an Identity session using a recovery code.");

        return endpoints;
    }
}
