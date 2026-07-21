using Identity.Api.Infrastructure;

namespace Identity.Api.Features.Accounts.Registering.V1;

internal static class RegisterAccountEndpointExtensions
{
    public static IEndpointRouteBuilder MapRegisterAccount(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/accounts/register",
                RegisterAccountEndpoint.HandleAsync)
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithName("RegisterIdentityAccount")
            .WithSummary("Register a customer account and send an email confirmation.");

        return endpoints;
    }
}
