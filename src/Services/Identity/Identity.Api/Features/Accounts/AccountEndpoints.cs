using Identity.Api.Features.Accounts.ConfirmingEmail.V1;
using Identity.Api.Features.Accounts.RecoveringPassword.V1;
using Identity.Api.Features.Accounts.Registering.V1;
using Identity.Api.Features.Sessions.LoggingIn.V1;

namespace Identity.Api.Features.Accounts;

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRegisterAccount();
        endpoints.MapConfirmEmail();
        endpoints.MapPasswordRecovery();
        endpoints.MapLogin();
        return endpoints;
    }
}
