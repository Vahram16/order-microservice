namespace Identity.Api.Notifications;

public interface IIdentityNotificationSender
{
    Task SendEmailConfirmationAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken);

    Task SendPasswordResetAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken);
}
