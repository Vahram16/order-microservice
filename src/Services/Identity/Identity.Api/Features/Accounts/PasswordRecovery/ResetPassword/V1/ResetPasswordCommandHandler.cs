using System.Text;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Features.Accounts.PasswordRecovery.ResetPassword.V1;

internal sealed class ResetPasswordCommandHandler(
    UserManager<ApplicationUser> userManager)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task Handle(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(
            command.UserId.ToString("D"));
        if (user is null || !user.IsActive)
        {
            throw new InvalidAccountTokenException();
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(
                WebEncoders.Base64UrlDecode(command.Code));
        }
        catch (FormatException)
        {
            throw new InvalidAccountTokenException();
        }

        var result = await userManager.ResetPasswordAsync(
            user,
            token,
            command.NewPassword);
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
