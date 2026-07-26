using Identity.Api.Features.Sessions.SigningIn;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Features.Sessions.SigningIn.Password.V1;

internal sealed class LoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    DummyPasswordVerifier dummyPasswordVerifier,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider)
    : IRequestHandler<LoginCommand, LoginOutcome>
{
    public async Task<LoginOutcome> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            dummyPasswordVerifier.Verify(command.Password);
            return LoginOutcome.Failed;
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            command.Password,
            isPersistent: false,
            lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            return LoginOutcome.RequiresTwoFactor;
        }

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
