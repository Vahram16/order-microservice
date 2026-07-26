namespace Identity.Api.Notifications;

internal interface IIdentityNotificationTransport
{
    Task SendAsync(
        IdentityNotificationPayload payload,
        CancellationToken cancellationToken);
}
