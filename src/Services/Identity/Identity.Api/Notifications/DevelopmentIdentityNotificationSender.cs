using Identity.Api.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

internal sealed partial class DevelopmentIdentityNotificationSender(
    IOptions<IdentityNotificationOptions> options,
    ILogger<DevelopmentIdentityNotificationSender> logger)
    : IIdentityNotificationSender
{
    private readonly IdentityNotificationOptions _options = options.Value;

    public Task EnqueueEmailConfirmationAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken)
    {
        var link = BuildLink("/account/confirm-email", userId, encodedToken);
        LogDevelopmentEmailConfirmation(logger, email, link);
        return Task.CompletedTask;
    }

    public Task EnqueuePasswordResetAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken)
    {
        var link = BuildLink("/account/reset-password", userId, encodedToken);
        LogDevelopmentPasswordReset(logger, email, link);
        return Task.CompletedTask;
    }

    private string BuildLink(string path, Guid userId, string token) =>
        QueryHelpers.AddQueryString(
            new Uri(new Uri(_options.PublicOrigin!), path).AbsoluteUri,
            new Dictionary<string, string?>
            {
                ["userId"] = userId.ToString("D"),
                ["code"] = token
            });

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "DEVELOPMENT ONLY confirmation for {Email}: {ConfirmationLink}")]
    private static partial void LogDevelopmentEmailConfirmation(
        ILogger logger,
        string email,
        string confirmationLink);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "DEVELOPMENT ONLY password reset for {Email}: {PasswordResetLink}")]
    private static partial void LogDevelopmentPasswordReset(
        ILogger logger,
        string email,
        string passwordResetLink);
}
