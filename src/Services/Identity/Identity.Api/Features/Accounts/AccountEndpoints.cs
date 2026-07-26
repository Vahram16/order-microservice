using Identity.Api.Features.Accounts.EmailConfirmation;
using Identity.Api.Features.Accounts.PasswordRecovery;
using Identity.Api.Features.Accounts.Registering.V1;

namespace Identity.Api.Features.Accounts;

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRegisterAccount();
        endpoints.MapEmailConfirmation();
        endpoints.MapPasswordRecovery();
        return endpoints;
    }
}
