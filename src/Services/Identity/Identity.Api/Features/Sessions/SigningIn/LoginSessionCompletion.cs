using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Features.Sessions.SigningIn;

internal static partial class LoginSessionCompletion
{
    public static async Task CompleteAsync(
        ApplicationUser user,
        SignInManager<ApplicationUser> signInManager,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var authenticationTime = timeProvider.GetUtcNow();
        var authenticationProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = false,
            IssuedUtc = authenticationTime
        };
        OidcAuthenticationState.SetAuthenticationTime(
            authenticationProperties,
            authenticationTime);
        await signInManager.SignInAsync(
            user,
            authenticationProperties,
            authenticationMethod: null);

        var loginLogger = loggerFactory.CreateLogger("Identity.Login");
        LogUserSignedIn(loginLogger, user.Id);
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Identity user {UserId} signed in")]
    private static partial void LogUserSignedIn(ILogger logger, Guid userId);
}
