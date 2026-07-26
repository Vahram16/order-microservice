using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;

namespace Notifications.Api.Features.IdentityNotifications.Receive.V1;

public sealed record ReceiveIdentityNotificationRequest(
    Guid EventId,
    string Template,
    string Recipient,
    string ActionUrl,
    DateTimeOffset ExpiresAtUtc);

public sealed record ReceiveIdentityNotificationCommand(
    string IdempotencyKey,
    Guid EventId,
    string Template,
    string Recipient,
    string ActionUrl,
    DateTimeOffset ExpiresAtUtc) : IRequest<NotificationAcceptanceResult>;

public enum NotificationAcceptanceResult
{
    Accepted,
    Duplicate
}

public sealed class ReceiveIdentityNotificationCommandValidator
    : AbstractValidator<ReceiveIdentityNotificationCommand>
{
    public ReceiveIdentityNotificationCommandValidator(
        TimeProvider timeProvider,
        IHostEnvironment environment,
        IOptions<NotificationDeliveryOptions> deliveryOptions)
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(128)
            .Must((command, value) => string.Equals(
                value,
                command.EventId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
            .WithMessage("The idempotency key must match the event identifier.");
        RuleFor(command => command.Template)
            .Must(IdentityNotificationTemplates.IsSupported)
            .WithMessage("The identity notification template is not supported.");
        RuleFor(command => command.Recipient)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
        RuleFor(command => command.ActionUrl)
            .NotEmpty()
            .MaximumLength(8192)
            .Must(value => IsSafeActionUrl(value, environment))
            .WithMessage("The action URL must be an absolute safe HTTP(S) URL.");
        RuleFor(command => command.ExpiresAtUtc)
            .Must(value => value > timeProvider.GetUtcNow())
            .WithMessage("The notification must not already be expired.")
            .Must(value => value <= timeProvider.GetUtcNow() +
                deliveryOptions.Value.MaximumNotificationLifetime)
            .WithMessage("The notification expiry exceeds the configured maximum lifetime.");
    }

    private static bool IsSafeActionUrl(string value, IHostEnvironment environment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
            environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp;
    }
}

internal static class IdentityNotificationTemplates
{
    public const string EmailConfirmation = "identity.email-confirmation";
    public const string PasswordReset = "identity.password-reset";

    public static bool IsSupported(string value) =>
        string.Equals(value, EmailConfirmation, StringComparison.Ordinal) ||
        string.Equals(value, PasswordReset, StringComparison.Ordinal);
}
