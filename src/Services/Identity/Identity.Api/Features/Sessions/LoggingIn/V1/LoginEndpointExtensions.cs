using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

internal static class LoginEndpointExtensions
{
    public static IEndpointRouteBuilder MapLogin(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/sessions",
                LoginEndpoint.PasswordAsync)
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
                LoginEndpoint.AuthenticatorAsync)
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
                LoginEndpoint.RecoveryCodeAsync)
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
