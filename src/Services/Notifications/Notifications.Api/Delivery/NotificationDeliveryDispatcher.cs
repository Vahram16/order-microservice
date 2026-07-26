using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;
using Notifications.Api.Email;
using Notifications.Api.Features.IdentityNotifications.Receive.V1;
using Notifications.Api.Persistence;

namespace Notifications.Api.Delivery;

internal sealed partial class NotificationDeliveryDispatcher(
    NotificationDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IEmailTransport emailTransport,
    IOptions<NotificationDeliveryOptions> deliveryOptions,
    IOptions<PostmarkOptions> postmarkOptions,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveryDispatcher> logger)
{
    private readonly NotificationDeliveryOptions _deliveryOptions = deliveryOptions.Value;
    private readonly PostmarkOptions _postmarkOptions = postmarkOptions.Value;
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Notifications.Api.IdentityNotificationPayload.v1");

    public async Task DispatchBatchAsync(
        bool cleanup,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.CreateVersion7();
        var deliveryIds = await LeaseBatchAsync(leaseId, now, cancellationToken);

        foreach (var deliveryId in deliveryIds)
        {
            var delivery = await dbContext.Deliveries.SingleOrDefaultAsync(
                candidate => candidate.Id == deliveryId && candidate.LockId == leaseId,
                cancellationToken);
            if (delivery is null)
            {
                continue;
            }

            try
            {
                if (delivery.ExpiresAtUtc <= timeProvider.GetUtcNow())
                {
                    throw new ExpiredNotificationException();
                }

                var json = _protector.Unprotect(delivery.ProtectedPayload);
                var payload = JsonSerializer.Deserialize<IdentityNotificationDeliveryPayload>(json) ??
                    throw new JsonException("The notification payload is empty.");
                var templateAlias = ResolveTemplateAlias(payload.Template);
                var result = await emailTransport.SendAsync(
                    new EmailMessage(
                        delivery.Id,
                        payload.EventId,
                        payload.Template,
                        templateAlias,
                        payload.Recipient,
                        payload.ActionUrl,
                        payload.ExpiresAtUtc),
                    cancellationToken);

                delivery.AcceptedByProviderAtUtc = timeProvider.GetUtcNow();
                delivery.ProviderMessageId = result.ProviderMessageId;
                delivery.ProtectedPayload = string.Empty;
                delivery.LockId = null;
                delivery.LockedUntilUtc = null;
                delivery.LastError = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                LogDelivered(logger, delivery.Id, result.ProviderMessageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await RecordFailureAsync(delivery, exception, cancellationToken);
            }
        }

        if (cleanup)
        {
            await CleanupAsync(now, cancellationToken);
        }
    }

    private string ResolveTemplateAlias(string template) => template switch
    {
        IdentityNotificationTemplates.EmailConfirmation =>
            _postmarkOptions.EmailConfirmationTemplateAlias,
        IdentityNotificationTemplates.PasswordReset =>
            _postmarkOptions.PasswordResetTemplateAlias,
        _ => throw new UnsupportedNotificationTemplateException()
    };

    private async Task RecordFailureAsync(
        NotificationDelivery delivery,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        delivery.AttemptCount++;
        delivery.LockId = null;
        delivery.LockedUntilUtc = null;
        delivery.LastError = GetSafeError(exception);

        var transient = exception is EmailTransportException { IsTransient: true };
        var nextAttempt = transient
            ? GetNextAttemptAtUtc(
                delivery.AttemptCount,
                _deliveryOptions.MaximumAttempts,
                now,
                delivery.ExpiresAtUtc)
            : null;

        if (nextAttempt is null)
        {
            delivery.DeadLetteredAtUtc = now;
            delivery.ProtectedPayload = string.Empty;
            LogDeadLettered(
                logger,
                delivery.Id,
                delivery.AttemptCount,
                delivery.LastError);
        }
        else
        {
            delivery.AvailableAtUtc = nextAttempt.Value;
            LogDeferred(
                logger,
                delivery.Id,
                delivery.AttemptCount,
                delivery.LastError);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private async Task<Guid[]> LeaseBatchAsync(
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH candidates AS
            (
                SELECT "Id"
                FROM notifications.deliveries
                WHERE "AcceptedByProviderAtUtc" IS NULL
                  AND "DeadLetteredAtUtc" IS NULL
                  AND "AvailableAtUtc" <= @now
                  AND ("LockedUntilUtc" IS NULL OR "LockedUntilUtc" < @now)
                ORDER BY "CreatedAtUtc"
                LIMIT @batch_size
                FOR UPDATE SKIP LOCKED
            )
            UPDATE notifications.deliveries AS target
            SET "LockId" = @lease_id,
                "LockedUntilUtc" = @locked_until
            FROM candidates
            WHERE target."Id" = candidates."Id"
            RETURNING target."Id";
            """;
        AddParameter(command, "now", DbType.DateTimeOffset, now);
        AddParameter(command, "batch_size", DbType.Int32, _deliveryOptions.BatchSize);
        AddParameter(command, "lease_id", DbType.Guid, leaseId);
        AddParameter(
            command,
            "locked_until",
            DbType.DateTimeOffset,
            now + _deliveryOptions.LeaseDuration);

        var ids = new List<Guid>(_deliveryOptions.BatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    private async Task CleanupAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var completedBefore = now - _deliveryOptions.CompletedRetention;
        var deadLetterBefore = now - _deliveryOptions.DeadLetterRetention;
        await dbContext.Deliveries
            .Where(delivery =>
                delivery.AcceptedByProviderAtUtc < completedBefore ||
                delivery.DeadLetteredAtUtc < deadLetterBefore)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string GetSafeError(Exception exception) => exception switch
    {
        EmailTransportException transport => transport.SafeError,
        ExpiredNotificationException => "NotificationExpired",
        CryptographicException => "PayloadDecryptionFailed",
        JsonException => "PayloadDeserializationFailed",
        UnsupportedNotificationTemplateException => "UnsupportedTemplate",
        _ => exception.GetType().Name
    };

    internal static DateTimeOffset? GetNextAttemptAtUtc(
        int completedAttempts,
        int maximumAttempts,
        DateTimeOffset now,
        DateTimeOffset expiresAtUtc)
    {
        if (completedAttempts >= maximumAttempts)
        {
            return null;
        }

        var delaySeconds = Math.Min(
            3600,
            15 * Math.Pow(2, Math.Min(completedAttempts - 1, 8)));
        var nextAttempt = now + TimeSpan.FromSeconds(delaySeconds);
        return nextAttempt < expiresAtUtc ? nextAttempt : null;
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Postmark accepted notification {NotificationId} as {ProviderMessageId}")]
    private static partial void LogDelivered(
        ILogger logger,
        Guid notificationId,
        string providerMessageId);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "Deferred notification {NotificationId} after attempt {Attempt}: {Error}")]
    private static partial void LogDeferred(
        ILogger logger,
        Guid notificationId,
        int attempt,
        string error);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Dead-lettered notification {NotificationId} after attempt {Attempt}: {Error}")]
    private static partial void LogDeadLettered(
        ILogger logger,
        Guid notificationId,
        int attempt,
        string error);

    private sealed class ExpiredNotificationException : Exception;
    private sealed class UnsupportedNotificationTemplateException : Exception;
}
