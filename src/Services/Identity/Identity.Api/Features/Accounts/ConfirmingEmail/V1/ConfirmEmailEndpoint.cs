using Identity.Api.Features.Presentation;
using Identity.Api.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal static class ConfirmEmailEndpoint
{
    private const string ConfirmEmailPath = "/account/confirm-email";

    public static IEndpointRouteBuilder MapConfirmEmail(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ConfirmEmailPath, RenderConfirmation)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(ConfirmEmailPath, ConfirmAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                "/api/v1/accounts/email-confirmation/resend",
                async (
                    ResendEmailConfirmationRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    await sender.Send(
                        new ResendEmailConfirmationCommand(request.Email),
                        cancellationToken);
                    return Results.Accepted();
                })
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithName("ResendIdentityEmailConfirmation")
            .WithSummary("Send another confirmation when an eligible account exists.");

        return endpoints;
    }

    private static IResult RenderConfirmation(
        HttpContext context,
        Guid userId,
        string code,
        IAntiforgery antiforgery,
        IdentityPageRenderer pageRenderer)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException(
                "An antiforgery token could not be created.");
        var page = pageRenderer.RenderConfirmEmail(
            new ConfirmEmailPageModel(userId, code, token));

        return Results.Content(page, "text/html; charset=utf-8");
    }

    private static async Task<IResult> ConfirmAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IdentityPageRenderer pageRenderer,
        ISender sender)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var request = new ConfirmEmailRequest(
            form["userId"].ToString(),
            form["code"].ToString());
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            return Results.BadRequest();
        }

        await sender.Send(
            new ConfirmEmailCommand(userId, request.Code),
            context.RequestAborted);

        return Results.Content(
            pageRenderer.RenderEmailConfirmed(),
            "text/html; charset=utf-8");
    }
}
