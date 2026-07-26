using System.Text;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Identity.Api.Notifications;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Features.Accounts.RecoveringPassword.V1;

internal sealed class RequestPasswordResetCommandHandler(
    UserManager<ApplicationUser> userManager,
    IIdentityNotificationSender notificationSender,
    TimeProvider timeProvider)
    : ICommandHandler<RequestPasswordResetCommand>
{
    public async Task Handle(
        RequestPasswordResetCommand command,
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
        await notificationSender.EnqueuePasswordResetAsync(
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
