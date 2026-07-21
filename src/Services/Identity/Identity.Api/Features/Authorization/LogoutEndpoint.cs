using Identity.Api.Features.Presentation;
using Identity.Api.Model;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;

namespace Identity.Api.Features.Authorization;

internal static class LogoutEndpoint
{
    public static IResult Render(
        HttpContext context,
        IAntiforgery antiforgery,
        IdentityPageRenderer pageRenderer)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException(
                "An antiforgery token could not be created.");
        var action = context.Request.PathBase +
            context.Request.Path +
            context.Request.QueryString;
        var page = pageRenderer.RenderLogout(new LogoutPageModel(action, token));

        return Results.Content(page, "text/html; charset=utf-8");
    }

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        await signInManager.SignOutAsync();
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
