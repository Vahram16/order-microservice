using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal static class ConfirmEmailEndpointExtensions
{
    public static IEndpointRouteBuilder MapConfirmEmail(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/accounts/email-confirmation",
                ConfirmEmailEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .WithName("ConfirmIdentityEmail")
            .WithSummary("Confirm an account email using a one-time token.");

        endpoints.MapPost(
                "/api/v1/accounts/email-confirmation/resend",
                ResendEmailConfirmationEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithName("ResendIdentityEmailConfirmation")
            .WithSummary("Send another confirmation when an eligible account exists.");

        return endpoints;
    }
}
