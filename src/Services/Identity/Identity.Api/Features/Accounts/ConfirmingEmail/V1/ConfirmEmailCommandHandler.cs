using System.Text;
using Identity.Api.Model;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal sealed class ConfirmEmailCommandHandler(
    UserManager<ApplicationUser> userManager)
    : ICommandHandler<ConfirmEmailCommand>
{
    public async Task Handle(
        ConfirmEmailCommand command,
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
