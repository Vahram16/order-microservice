using System.Text;
using Identity.Api.Model;
using Identity.Api.Notifications;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal sealed class ResendEmailConfirmationCommandHandler(
    UserManager<ApplicationUser> userManager,
    IIdentityNotificationSender notificationSender,
    TimeProvider timeProvider)
    : ICommandHandler<ResendEmailConfirmationCommand>
{
    public async Task Handle(
        ResendEmailConfirmationCommand command,
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
