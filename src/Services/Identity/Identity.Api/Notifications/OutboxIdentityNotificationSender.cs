using Identity.Api.Configuration;
using Identity.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

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

    public Task EnqueueEmailConfirmationAsync(
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

    public Task EnqueuePasswordResetAsync(
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
