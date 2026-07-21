using Identity.Api.Features.Presentation;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal static class ConfirmEmailEndpoint
{
    public static IResult Render(
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

    public static async Task<IResult> HandleAsync(
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
