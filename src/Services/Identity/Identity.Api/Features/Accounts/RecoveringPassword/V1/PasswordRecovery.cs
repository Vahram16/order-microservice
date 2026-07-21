using System.Text;
using FluentValidation;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Identity.Api.Notifications;
using MediatR;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Features.Accounts.RecoveringPassword.V1;

public sealed record RequestPasswordReset(string Email) : ICommand;

public sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordReset>
{
    public RequestPasswordResetValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
}

internal sealed class RequestPasswordResetHandler(
    UserManager<ApplicationUser> userManager,
    IIdentityNotificationSender notificationSender,
    TimeProvider timeProvider)
    : ICommandHandler<RequestPasswordReset>
{
    public async Task Handle(
        RequestPasswordReset command,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        var user = await userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null || !user.IsActive || !user.EmailConfirmed || user.Email is null)
        {
            await AccountEnumerationResistance.CompleteAsync(
                timeProvider,
                startedAt,
                cancellationToken);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        await notificationSender.SendPasswordResetAsync(
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

public sealed record ResetPassword(
    Guid UserId,
    string Code,
    string NewPassword) : ICommand;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPassword>
{
    public ResetPasswordValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty();
        RuleFor(command => command.NewPassword)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(128);
    }
}

internal sealed class ResetPasswordHandler(UserManager<ApplicationUser> userManager)
    : ICommandHandler<ResetPassword>
{
    public async Task Handle(
        ResetPassword command,
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

        var result = await userManager.ResetPasswordAsync(user, token, command.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "InvalidToken"))
            {
                throw new InvalidAccountTokenException();
            }

            throw new IdentityOperationException(result.Errors);
        }

        await userManager.UpdateSecurityStampAsync(user);
    }
}

internal static class PasswordRecoveryEndpoint
{
    public static IEndpointRouteBuilder MapPasswordRecovery(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/accounts/password-reset/request",
                async (RequestPasswordReset request, ISender sender, CancellationToken cancellationToken) =>
                {
                    await sender.Send(request, cancellationToken);
                    return Results.Accepted();
                })
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithName("RequestIdentityPasswordReset")
            .WithSummary("Send password recovery instructions when an eligible account exists.");

        endpoints.MapPost(
                "/api/v1/accounts/password-reset",
                async (ResetPassword request, ISender sender, CancellationToken cancellationToken) =>
                {
                    await sender.Send(request, cancellationToken);
                    return Results.NoContent();
                })
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .WithName("ResetIdentityPassword")
            .WithSummary("Reset a password using a one-time recovery token.");

        return endpoints;
    }
}
