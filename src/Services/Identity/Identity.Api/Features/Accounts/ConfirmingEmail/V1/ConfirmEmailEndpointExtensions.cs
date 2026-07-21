using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal static class ConfirmEmailEndpointExtensions
{
    private const string ConfirmEmailPath = "/account/confirm-email";

    public static IEndpointRouteBuilder MapConfirmEmail(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ConfirmEmailPath, ConfirmEmailEndpoint.Render)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(ConfirmEmailPath, ConfirmEmailEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

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
