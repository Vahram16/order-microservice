using Identity.Api.Features.Accounts.EmailConfirmation.Confirm.V1;
using Identity.Api.Features.Accounts.EmailConfirmation.Resend.V1;
using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Accounts.EmailConfirmation;

internal static class EmailConfirmationEndpointExtensions
{
    public static IEndpointRouteBuilder MapEmailConfirmation(
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
