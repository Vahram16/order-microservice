using Identity.Api.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;

namespace Identity.Api.Features.Authorization;

internal static class LogoutEndpoint
{
    public static IResult Begin(
        HttpContext context,
        LogoutInteractionProtector interactionProtector,
        IdentityInteractionUrlBuilder interactionUrlBuilder)
    {
        _ = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "The OpenID Connect end-session request cannot be retrieved.");

        var completionUri = GetLocalRequestUri(context.Request);
        var interactionToken = interactionProtector.Protect(completionUri);

        return Results.Redirect(
            interactionUrlBuilder.CreateLogoutUri(
                interactionToken,
                completionUri));
    }

    public static async Task<IResult> CompleteAsync(
        HttpContext context,
        LogoutConfirmationRequest request,
        LogoutInteractionProtector interactionProtector,
        SignInManager<ApplicationUser> signInManager)
    {
        _ = context.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException(
                "The OpenID Connect end-session request cannot be retrieved.");

        var completionUri = GetLocalRequestUri(context.Request);
        if (!interactionProtector.IsValid(
                request.InteractionToken,
                completionUri))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(request.InteractionToken)] =
                    ["The logout interaction is invalid or has expired."]
                });
        }

        await signInManager.SignOutAsync();

        return Results.SignOut(
            new AuthenticationProperties(),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static string GetLocalRequestUri(HttpRequest request) =>
        request.PathBase + request.Path + request.QueryString;
}
