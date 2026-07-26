using Identity.Api.Features.Sessions.SigningIn;
using Identity.Api.Model;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Features.Sessions.SigningIn.Authenticator.V1;

internal sealed class AuthenticatorCodeCommandHandler(
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider)
    : IRequestHandler<AuthenticatorCodeCommand, LoginOutcome>
{
    public async Task<LoginOutcome> Handle(
        AuthenticatorCodeCommand command,
        CancellationToken cancellationToken)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsActive)
        {
            return LoginOutcome.Failed;
        }

        var code = command.Code
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code,
            isPersistent: false,
            rememberClient: false);
        if (!result.Succeeded)
        {
            return LoginOutcome.Failed;
        }

        await LoginSessionCompletion.CompleteAsync(
            user,
            signInManager,
            loggerFactory,
            timeProvider);
        return LoginOutcome.Succeeded;
    }
}
