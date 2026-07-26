namespace Identity.Api.Notifications;

public interface IIdentityNotificationSender
{
    Task EnqueueEmailConfirmationAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken);

    Task EnqueuePasswordResetAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken);
}
