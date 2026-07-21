using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Accounts.RecoveringPassword.V1;

internal static class PasswordRecoveryEndpointExtensions
{
    public static IEndpointRouteBuilder MapPasswordRecovery(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/accounts/password-reset/request",
                RequestPasswordResetEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithName("RequestIdentityPasswordReset")
            .WithSummary("Send password recovery instructions when an eligible account exists.");

        endpoints.MapPost(
                "/api/v1/accounts/password-reset",
                ResetPasswordEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .WithName("ResetIdentityPassword")
            .WithSummary("Reset a password using a one-time recovery token.");

        return endpoints;
    }
}
