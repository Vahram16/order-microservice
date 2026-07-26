using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Notifications.Api.Persistence;

namespace Notifications.Api.Features.IdentityNotifications.Receive.V1;

internal sealed class ReceiveIdentityNotificationCommandHandler(
    NotificationDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
    : IRequestHandler<ReceiveIdentityNotificationCommand, NotificationAcceptanceResult>
{
    private const string Source = "identity";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Notifications.Api.IdentityNotificationPayload.v1");

    public async Task<NotificationAcceptanceResult> Handle(
        ReceiveIdentityNotificationCommand command,
        CancellationToken cancellationToken)
    {
        var payload = new IdentityNotificationDeliveryPayload(
            command.EventId,
            command.Template,
            command.Recipient.Trim(),
            command.ActionUrl,
            command.ExpiresAtUtc);
        var json = JsonSerializer.Serialize(payload);
        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        var existing = await dbContext.Deliveries.SingleOrDefaultAsync(
            delivery => delivery.Source == Source &&
                delivery.SourceEventId == command.EventId,
            cancellationToken);
        if (existing is not null)
        {
            return ResolveDuplicate(existing, command.IdempotencyKey, payloadHash);
        }

        var now = timeProvider.GetUtcNow();
        dbContext.Deliveries.Add(new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            Source = Source,
            SourceEventId = command.EventId,
            IdempotencyKey = command.IdempotencyKey.ToLowerInvariant(),
            Template = command.Template,
            ProtectedPayload = _protector.Protect(json),
            PayloadHash = payloadHash,
            ExpiresAtUtc = command.ExpiresAtUtc,
            CreatedAtUtc = now,
            AvailableAtUtc = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return NotificationAcceptanceResult.Accepted;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            existing = await dbContext.Deliveries.SingleOrDefaultAsync(
                delivery => delivery.Source == Source &&
                    delivery.SourceEventId == command.EventId,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return ResolveDuplicate(existing, command.IdempotencyKey, payloadHash);
        }
    }

    private static NotificationAcceptanceResult ResolveDuplicate(
        NotificationDelivery existing,
        string idempotencyKey,
        string payloadHash)
    {
        if (!string.Equals(
                existing.IdempotencyKey,
                idempotencyKey,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw new ConflictingNotificationIdempotencyException();
        }

        return NotificationAcceptanceResult.Duplicate;
    }
}

internal sealed record IdentityNotificationDeliveryPayload(
    Guid EventId,
    string Template,
    string Recipient,
    string ActionUrl,
    DateTimeOffset ExpiresAtUtc);

internal sealed class ConflictingNotificationIdempotencyException()
    : Exception("The event identifier was reused with a different notification payload.");
