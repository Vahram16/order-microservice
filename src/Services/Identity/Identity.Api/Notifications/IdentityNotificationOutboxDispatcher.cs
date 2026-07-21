using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Identity.Api.Configuration;
using Identity.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

internal sealed partial class IdentityNotificationOutboxDispatcher(
    IdentityServiceDbContext dbContext,
    IIdentityNotificationTransport transport,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<IdentityNotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityNotificationOutboxDispatcher> logger)
{
    private readonly IdentityNotificationOptions _options = options.Value;
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Identity.Api.NotificationOutbox.v1");

    public async Task DispatchBatchAsync(
        bool cleanupExpiredRecords,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.CreateVersion7();
        var messageIds = await LeaseBatchAsync(leaseId, now, cancellationToken);

        foreach (var messageId in messageIds)
        {
            var message = await dbContext.NotificationOutbox.SingleOrDefaultAsync(
                candidate => candidate.Id == messageId && candidate.LockId == leaseId,
                cancellationToken);
            if (message is null)
            {
                continue;
            }

            IdentityNotificationPayload? payload = null;
            try
            {
                var json = _protector.Unprotect(message.ProtectedPayload);
                payload = JsonSerializer.Deserialize<IdentityNotificationPayload>(json) ??
                    throw new JsonException("The notification payload is empty.");
                if (payload.ExpiresAtUtc <= timeProvider.GetUtcNow())
                {
                    throw new ExpiredIdentityNotificationException();
                }

                await transport.SendAsync(payload, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await RecordFailureAsync(
                    message,
                    payload?.ExpiresAtUtc,
                    exception,
                    cancellationToken);
                continue;
            }

            message.ProcessedAtUtc = timeProvider.GetUtcNow();
            message.ProtectedPayload = string.Empty;
            message.LockId = null;
            message.LockedUntilUtc = null;
            message.LastError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            LogNotificationDelivered(logger, message.Id);
        }

        if (cleanupExpiredRecords)
        {
            await CleanupAsync(now, cancellationToken);
        }
    }

    private async Task RecordFailureAsync(
        IdentityNotificationOutboxMessage message,
        DateTimeOffset? expiresAtUtc,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        message.AttemptCount++;
        message.LockId = null;
        message.LockedUntilUtc = null;
        message.LastError = GetSafeError(exception);
        var nextAttemptAtUtc = IsPermanent(exception)
            ? null
            : GetNextAttemptAtUtc(
                message.AttemptCount,
                _options.MaximumAttempts,
                now,
                expiresAtUtc);

        if (nextAttemptAtUtc is null)
        {
            message.DeadLetteredAtUtc = now;
            message.ProtectedPayload = string.Empty;
            LogNotificationDeadLettered(
                logger,
                message.Id,
                message.AttemptCount,
                message.LastError);
        }
        else
        {
            message.AvailableAtUtc = nextAttemptAtUtc.Value;
            LogNotificationDeferred(
                logger,
                message.Id,
                message.AttemptCount,
                message.LastError);
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
                FROM identity.notification_outbox
                WHERE "ProcessedAtUtc" IS NULL
                  AND "DeadLetteredAtUtc" IS NULL
                  AND "AvailableAtUtc" <= @now
                  AND ("LockedUntilUtc" IS NULL OR "LockedUntilUtc" < @now)
                ORDER BY "CreatedAtUtc"
                LIMIT @batch_size
                FOR UPDATE SKIP LOCKED
            )
            UPDATE identity.notification_outbox AS target
            SET "LockId" = @lease_id,
                "LockedUntilUtc" = @locked_until
            FROM candidates
            WHERE target."Id" = candidates."Id"
            RETURNING target."Id";
            """;
        AddParameter(command, "now", DbType.DateTimeOffset, now);
        AddParameter(command, "batch_size", DbType.Int32, _options.BatchSize);
        AddParameter(command, "lease_id", DbType.Guid, leaseId);
        AddParameter(
            command,
            "locked_until",
            DbType.DateTimeOffset,
            now + _options.LeaseDuration);

        var messageIds = new List<Guid>(_options.BatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messageIds.Add(reader.GetGuid(0));
        }

        return [.. messageIds];
    }

    private async Task CleanupAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var completedRetention = now - TimeSpan.FromDays(7);
        var deadLetterRetention = now - TimeSpan.FromDays(30);
        await dbContext.NotificationOutbox
            .Where(message =>
                message.ProcessedAtUtc < completedRetention ||
                message.DeadLetteredAtUtc < deadLetterRetention)
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

    private static bool IsPermanent(Exception exception) =>
        exception is CryptographicException or JsonException or ExpiredIdentityNotificationException ||
        exception is HttpRequestException
        {
            StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError and
                not HttpStatusCode.RequestTimeout and
                not HttpStatusCode.TooManyRequests
        };

    private static string GetSafeError(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } statusCode }
            ? $"HTTP {(int)statusCode}"
            : exception.GetType().Name;

    internal static DateTimeOffset? GetNextAttemptAtUtc(
        int completedAttempts,
        int maximumAttempts,
        DateTimeOffset now,
        DateTimeOffset? expiresAtUtc)
    {
        if (completedAttempts >= maximumAttempts)
        {
            return null;
        }

        var nextAttemptAtUtc = now + GetRetryDelay(completedAttempts);
        return expiresAtUtc is { } expiresAt && nextAttemptAtUtc >= expiresAt
            ? null
            : nextAttemptAtUtc;
    }

    internal static TimeSpan GetRetryDelay(int completedAttempts)
    {
        var seconds = Math.Min(
            3600,
            10 * Math.Pow(2, Math.Min(completedAttempts - 1, 8)));
        return TimeSpan.FromSeconds(seconds);
    }

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Debug,
        Message = "Delivered identity notification {NotificationId}")]
    private static partial void LogNotificationDelivered(
        ILogger logger,
        Guid notificationId);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Warning,
        Message = "Deferred identity notification {NotificationId} after attempt {Attempt}: {Error}")]
    private static partial void LogNotificationDeferred(
        ILogger logger,
        Guid notificationId,
        int attempt,
        string error);

    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Error,
        Message = "Dead-lettered identity notification {NotificationId} after attempt {Attempt}: {Error}")]
    private static partial void LogNotificationDeadLettered(
        ILogger logger,
        Guid notificationId,
        int attempt,
        string error);

    private sealed class ExpiredIdentityNotificationException()
        : Exception("The account notification token expired before delivery.");
}
