namespace Notifications.Api.Email;

internal interface IEmailTransport
{
    Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken);
}

internal sealed record EmailMessage(
    Guid NotificationId,
    Guid SourceEventId,
    string Template,
    string TemplateAlias,
    string Recipient,
    string ActionUrl,
    DateTimeOffset ExpiresAtUtc);

internal sealed record EmailDeliveryResult(string ProviderMessageId);

internal sealed class EmailTransportException(
    string safeError,
    bool isTransient,
    Exception? innerException = null)
    : Exception(safeError, innerException)
{
    public string SafeError { get; } = safeError;
    public bool IsTransient { get; } = isTransient;
}
