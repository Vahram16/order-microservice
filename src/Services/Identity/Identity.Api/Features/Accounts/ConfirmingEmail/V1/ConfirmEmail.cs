using System.Text;
using System.Text.Encodings.Web;
using FluentValidation;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using MediatR;
using Microservices.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

public sealed record ConfirmEmail(Guid UserId, string Code) : ICommand;

internal sealed class ConfirmEmailHandler(UserManager<ApplicationUser> userManager)
    : ICommandHandler<ConfirmEmail>
{
    public async Task Handle(
        ConfirmEmail command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString("D"));
        if (user is null || !user.IsActive)
        {
            throw new InvalidAccountTokenException();
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Code));
        }
        catch (FormatException)
        {
            throw new InvalidAccountTokenException();
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw new InvalidAccountTokenException();
        }
    }
}

public sealed record ResendEmailConfirmation(string Email) : ICommand;

public sealed class ResendEmailConfirmationValidator
    : AbstractValidator<ResendEmailConfirmation>
{
    public ResendEmailConfirmationValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
}

internal sealed class ResendEmailConfirmationHandler(
    UserManager<ApplicationUser> userManager,
    Notifications.IIdentityNotificationSender notificationSender,
    TimeProvider timeProvider)
    : ICommandHandler<ResendEmailConfirmation>
{
    public async Task Handle(
        ResendEmailConfirmation command,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        var user = await userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null || !user.IsActive || user.EmailConfirmed || user.Email is null)
        {
            await AccountEnumerationResistance.CompleteAsync(
                timeProvider,
                startedAt,
                cancellationToken);
            return;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        await notificationSender.SendEmailConfirmationAsync(
            user.Email,
            user.Id,
            encodedToken,
            cancellationToken);
        await AccountEnumerationResistance.CompleteAsync(
            timeProvider,
            startedAt,
            cancellationToken);
    }
}

internal static class ConfirmEmailEndpoint
{
    private const string ConfirmEmailPath = "/account/confirm-email";

    public static IEndpointRouteBuilder MapConfirmEmail(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                ConfirmEmailPath,
                RenderConfirmation)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                ConfirmEmailPath,
                ConfirmAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                "/api/v1/accounts/email-confirmation/resend",
                async (ResendEmailConfirmation request, ISender sender, CancellationToken cancellationToken) =>
                {
                    await sender.Send(request, cancellationToken);
                    return Results.Accepted();
                })
            .AllowAnonymous()
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .WithName("ResendIdentityEmailConfirmation")
            .WithSummary("Send another confirmation when an eligible account exists.");

        return endpoints;
    }

    private static IResult RenderConfirmation(
        HttpContext context,
        Guid userId,
        string code,
        IAntiforgery antiforgery)
    {
        var encoder = HtmlEncoder.Default;
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException(
                "An antiforgery token could not be created.");
        var content = $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Confirm email</title>
            </head>
            <body>
              <main>
                <h1>Confirm your email</h1>
                <p>Confirm that you want to activate this account.</p>
                <form method="post" action="{ConfirmEmailPath}">
                  <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
                  <input type="hidden" name="userId" value="{userId:D}">
                  <input type="hidden" name="code" value="{encoder.Encode(code)}">
                  <button type="submit">Confirm email</button>
                </form>
              </main>
            </body>
            </html>
            """;
        return Results.Content(content, "text/html; charset=utf-8");
    }

    private static async Task<IResult> ConfirmAsync(
        HttpContext context,
        IAntiforgery antiforgery,
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
        if (!Guid.TryParse(form["userId"], out var userId) ||
            string.IsNullOrWhiteSpace(form["code"]))
        {
            return Results.BadRequest();
        }

        await sender.Send(
            new ConfirmEmail(userId, form["code"].ToString()),
            context.RequestAborted);
        return Results.Content(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Email confirmed</title></head><body><main><h1>Email confirmed</h1><p>You can return to the application and sign in.</p></main></body></html>",
            "text/html; charset=utf-8");
    }
}
