namespace Identity.Api.Notifications;

internal sealed record IdentityNotificationPayload(
    Guid EventId,
    string Template,
    string Recipient,
    string ActionUrl,
    DateTimeOffset ExpiresAtUtc);
