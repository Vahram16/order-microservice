using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;

namespace Identity.Api.Features.Authorization;

internal static class LogoutEndpoint
{
    public static IResult Handle() =>
        Results.SignOut(
            authenticationSchemes:
            [
                IdentityConstants.ApplicationScheme,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
            ]);
}
