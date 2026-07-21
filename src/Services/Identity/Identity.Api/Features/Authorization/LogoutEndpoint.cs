using Identity.Api.Model;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;

namespace Identity.Api.Features.Authorization;

internal static class LogoutEndpoint
{
    public static async Task<IResult> HandleAsync(
        SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();

        return Results.SignOut(
            authenticationSchemes:
            [
                IdentityConstants.ApplicationScheme,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
            ]);
    }
}
