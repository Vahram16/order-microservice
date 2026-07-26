using Microsoft.Extensions.Options;

namespace Notifications.Api.Configuration;

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "NotificationDelivery";

    public int BatchSize { get; init; } = 20;
    public int MaximumAttempts { get; init; } = 8;
    public TimeSpan DispatchInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan CompletedRetention { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan DeadLetterRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan MaximumNotificationLifetime { get; init; } = TimeSpan.FromHours(24);
}

public sealed class NotificationsIngressOptions
{
    public const string SectionName = "NotificationsIngress";

    public string? ApiKey { get; init; }
}

public sealed class PostmarkOptions
{
    public const string SectionName = "Postmark";

    public string ApiBaseAddress { get; init; } = "https://api.postmarkapp.com/";
    public string? ServerToken { get; init; }
    public string? FromAddress { get; init; }
    public string MessageStream { get; init; } = "outbound";
    public string EmailConfirmationTemplateAlias { get; init; } =
        "identity-email-confirmation-v1";
    public string PasswordResetTemplateAlias { get; init; } =
        "identity-password-reset-v1";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}

internal sealed class NotificationDeliveryOptionsValidator
    : IValidateOptions<NotificationDeliveryOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        NotificationDeliveryOptions options)
    {
        var failures = new List<string>();
        if (options.BatchSize is < 1 or > 200)
        {
            failures.Add("'NotificationDelivery:BatchSize' must be between 1 and 200.");
        }

        if (options.MaximumAttempts is < 1 or > 20)
        {
            failures.Add("'NotificationDelivery:MaximumAttempts' must be between 1 and 20.");
        }

        if (options.DispatchInterval < TimeSpan.FromMilliseconds(250) ||
            options.DispatchInterval > TimeSpan.FromMinutes(5))
        {
            failures.Add("'NotificationDelivery:DispatchInterval' must be between 250 milliseconds and five minutes.");
        }

        if (options.LeaseDuration < TimeSpan.FromSeconds(15) ||
            options.LeaseDuration > TimeSpan.FromMinutes(15))
        {
            failures.Add("'NotificationDelivery:LeaseDuration' must be between 15 seconds and 15 minutes.");
        }

        if (options.MaximumNotificationLifetime < TimeSpan.FromMinutes(5) ||
            options.MaximumNotificationLifetime > TimeSpan.FromDays(7))
        {
            failures.Add("'NotificationDelivery:MaximumNotificationLifetime' must be between five minutes and seven days.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class NotificationsIngressOptionsValidator(
    IHostEnvironment environment)
    : IValidateOptions<NotificationsIngressOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        NotificationsIngressOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length < 32)
        {
            return ValidateOptionsResult.Fail(
                "'NotificationsIngress:ApiKey' must contain at least 32 characters.");
        }

        if (!environment.IsDevelopment() &&
            options.ApiKey.StartsWith("local-development-", StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "The local development ingress API key is forbidden outside Development.");
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class PostmarkOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<PostmarkOptions>
{
    public ValidateOptionsResult Validate(string? name, PostmarkOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.ApiBaseAddress, UriKind.Absolute, out var api) ||
            api.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("'Postmark:ApiBaseAddress' must be an absolute HTTPS URI.");
        }

        if (string.IsNullOrWhiteSpace(options.ServerToken))
        {
            failures.Add("'Postmark:ServerToken' is required.");
        }
        else if (!environment.IsDevelopment() &&
                 string.Equals(options.ServerToken, "POSTMARK_API_TEST", StringComparison.Ordinal))
        {
            failures.Add("The Postmark test token is forbidden outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) ||
            !System.Net.Mail.MailAddress.TryCreate(options.FromAddress, out _))
        {
            failures.Add("'Postmark:FromAddress' must be a valid email address.");
        }

        ValidateRequired(options.MessageStream, "MessageStream", failures);
        ValidateRequired(options.EmailConfirmationTemplateAlias,
            "EmailConfirmationTemplateAlias", failures);
        ValidateRequired(options.PasswordResetTemplateAlias,
            "PasswordResetTemplateAlias", failures);

        if (options.Timeout < TimeSpan.FromSeconds(2) ||
            options.Timeout > TimeSpan.FromMinutes(2))
        {
            failures.Add("'Postmark:Timeout' must be between two seconds and two minutes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRequired(
        string value,
        string name,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
        {
            failures.Add($"'Postmark:{name}' is required and cannot exceed 100 characters.");
        }
    }
}
