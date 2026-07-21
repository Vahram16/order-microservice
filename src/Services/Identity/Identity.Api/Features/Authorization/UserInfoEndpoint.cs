using Identity.Api.Model;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

internal static class UserInfoEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var subject = context.User.GetClaim(Claims.Subject);
        var user = string.IsNullOrWhiteSpace(subject)
            ? null
            : await userManager.FindByIdAsync(subject);
        if (user is null || !user.IsActive || !await signInManager.CanSignInAsync(user))
        {
            return Results.Unauthorized();
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Claims.Subject] = user.Id.ToString("D")
        };
        if (context.User.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName;
            claims[Claims.PreferredUsername] = user.UserName;
        }

        if (context.User.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (context.User.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await userManager.GetRolesAsync(user);
        }

        return Results.Ok(claims);
    }
}
