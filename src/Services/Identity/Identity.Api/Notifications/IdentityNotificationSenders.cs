using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Api.Configuration;
using Identity.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

internal sealed partial class DevelopmentIdentityNotificationSender(
    IOptions<IdentityNotificationOptions> options,
    ILogger<DevelopmentIdentityNotificationSender> logger)
    : IIdentityNotificationSender
{
    private readonly IdentityNotificationOptions _options = options.Value;

    public Task SendEmailConfirmationAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken)
    {
        var link = BuildLink("/account/confirm-email", userId, encodedToken);
        LogDevelopmentEmailConfirmation(logger, email, link);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken)
    {
        var link = BuildLink("/account/reset-password", userId, encodedToken);
        LogDevelopmentPasswordReset(logger, email, link);
        return Task.CompletedTask;
    }

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

    private string BuildLink(string path, Guid userId, string token) =>
        QueryHelpers.AddQueryString(
            new Uri(new Uri(_options.PublicOrigin!), path).AbsoluteUri,
            new Dictionary<string, string?>
            {
                ["userId"] = userId.ToString("D"),
                ["code"] = token
            });
}

internal sealed class OutboxIdentityNotificationSender(
    IdentityServiceDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<IdentityNotificationOptions> options,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions,
    TimeProvider timeProvider)
    : IIdentityNotificationSender
{
    private const string EmailConfirmationTemplate = "identity.email-confirmation";
    private const string PasswordResetTemplate = "identity.password-reset";
    private readonly IdentityNotificationOptions _options = options.Value;
    private readonly TimeSpan _tokenLifespan = tokenOptions.Value.TokenLifespan;
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Identity.Api.NotificationOutbox.v1");

    public Task SendEmailConfirmationAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken) =>
        EnqueueAsync(
            EmailConfirmationTemplate,
            email,
            userId,
            BuildLink("/account/confirm-email", userId, encodedToken),
            cancellationToken);

    public Task SendPasswordResetAsync(
        string email,
        Guid userId,
        string encodedToken,
        CancellationToken cancellationToken) =>
        EnqueueAsync(
            PasswordResetTemplate,
            email,
            userId,
            BuildLink("/account/reset-password", userId, encodedToken),
            cancellationToken);

    private async Task EnqueueAsync(
        string template,
        string recipient,
        Guid userId,
        string actionUrl,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        var expiresAtUtc = now + _tokenLifespan;
        var payload = _protector.Protect(
            System.Text.Json.JsonSerializer.Serialize(
                new IdentityNotificationPayload(
                    id,
                    template,
                    recipient,
                    actionUrl,
                    expiresAtUtc)));
        var deduplicationKey = $"{template}:{userId:N}";
        var resendAfter = now - _options.DeduplicationWindow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO identity.notification_outbox AS target
                ("Id", "DeduplicationKey", "ProtectedPayload", "CreatedAtUtc",
                 "AvailableAtUtc", "AttemptCount")
            VALUES
                ({{id}}, {{deduplicationKey}}, {{payload}}, {{now}}, {{now}}, 0)
            ON CONFLICT ("DeduplicationKey") DO UPDATE
            SET "Id" = EXCLUDED."Id",
                "ProtectedPayload" = EXCLUDED."ProtectedPayload",
                "CreatedAtUtc" = EXCLUDED."CreatedAtUtc",
                "AvailableAtUtc" = EXCLUDED."AvailableAtUtc",
                "LockedUntilUtc" = NULL,
                "LockId" = NULL,
                "AttemptCount" = 0,
                "ProcessedAtUtc" = NULL,
                "DeadLetteredAtUtc" = NULL,
                "LastError" = NULL
            WHERE target."DeadLetteredAtUtc" IS NOT NULL
               OR target."ProcessedAtUtc" <= {{resendAfter}}
               OR target."CreatedAtUtc" <= {{now - _tokenLifespan}};
            """, cancellationToken);
    }

    private string BuildLink(string path, Guid userId, string token) =>
        QueryHelpers.AddQueryString(
            new Uri(new Uri(_options.PublicOrigin!), path).AbsoluteUri,
            new Dictionary<string, string?>
            {
                ["userId"] = userId.ToString("D"),
                ["code"] = token
            });
}

internal interface IIdentityNotificationTransport
{
    Task SendAsync(
        IdentityNotificationPayload payload,
        CancellationToken cancellationToken);
}

internal sealed class WebhookIdentityNotificationTransport(
    HttpClient httpClient,
    IOptions<IdentityNotificationOptions> options)
    : IIdentityNotificationTransport
{
    private readonly IdentityNotificationOptions _options = options.Value;

    public async Task SendAsync(
        IdentityNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.WebhookEndpoint)
        {
            Content = JsonContent.Create(new
            {
                eventId = payload.EventId,
                template = payload.Template,
                recipient = payload.Recipient,
                actionUrl = payload.ActionUrl,
                expiresAtUtc = payload.ExpiresAtUtc
            })
        };
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            payload.EventId.ToString("N"));

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.WebhookApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed record IdentityNotificationPayload(
    Guid EventId,
    string Template,
    string Recipient,
    string ActionUrl,
    DateTimeOffset ExpiresAtUtc);
