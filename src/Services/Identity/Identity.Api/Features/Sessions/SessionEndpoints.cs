using Identity.Api.Features.Sessions.SigningIn;

namespace Identity.Api.Features.Sessions;

internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSignIn();
        return endpoints;
    }
}
