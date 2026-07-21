using System.Text;
using FluentValidation;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using MediatR;
using Microservices.Application;
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
    public static IEndpointRouteBuilder MapConfirmEmail(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/account/confirm-email",
                async (Guid userId, string code, ISender sender, CancellationToken cancellationToken) =>
                {
                    await sender.Send(new ConfirmEmail(userId, code), cancellationToken);
                    return Results.Content(
                        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Email confirmed</title></head><body><main><h1>Email confirmed</h1><p>You can return to the application and sign in.</p></main></body></html>",
                        "text/html; charset=utf-8");
                })
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
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .WithName("ResendIdentityEmailConfirmation")
            .WithSummary("Send another confirmation when an eligible account exists.");

        return endpoints;
    }
}
