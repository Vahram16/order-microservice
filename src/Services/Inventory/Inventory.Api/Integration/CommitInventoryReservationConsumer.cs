using Inventory.Api.Domain;
using Inventory.Api.Persistence;
using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Integration;

internal sealed class CommitInventoryReservationConsumer(
    InventoryDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<CommitInventoryReservation>
{
    public async Task Consume(ConsumeContext<CommitInventoryReservation> context)
    {
        var reservation = await dbContext.Reservations
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == context.Message.ReservationId && item.OrderId == context.Message.OrderId, context.CancellationToken)
            ?? throw new InventoryWorkflowException("inventory.reservation_not_found");
        if (reservation.Status == InventoryReservationStatus.Committed)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (reservation.Status != InventoryReservationStatus.Active || reservation.ExpiresAt <= now)
        {
            if (reservation.Status == InventoryReservationStatus.Active)
            {
                await ExpireAsync(reservation, now, context.CancellationToken);
            }

            await eventPublisher.PublishAsync(
                new InventoryReservationExpired(reservation.OrderId, reservation.Id, now),
                new IntegrationMessageMetadata(CorrelationId: reservation.OrderId),
                context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var productIds = reservation.Lines.Select(item => item.ProductId).ToArray();
        var inventory = await dbContext.InventoryItems
            .Where(item => productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, context.CancellationToken);
        foreach (var line in reservation.Lines)
        {
            if (!inventory.TryGetValue(line.ProductId, out var item))
            {
                throw new InventoryWorkflowException("inventory.reservation_item_missing");
            }

            var commit = item.Commit(line.Quantity, now);
            if (commit.IsFailure)
            {
                throw new InventoryWorkflowException(commit.Error.Code);
            }
        }

        var transition = reservation.Commit(now);
        if (transition.IsFailure)
        {
            throw new InventoryWorkflowException(transition.Error.Code);
        }

        await eventPublisher.PublishAsync(
            new InventoryReservationCommitted(reservation.OrderId, reservation.Id, now),
            new IntegrationMessageMetadata(CorrelationId: reservation.OrderId),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }

    private async Task ExpireAsync(InventoryReservation reservation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var productIds = reservation.Lines.Select(item => item.ProductId).ToArray();
        var inventory = await dbContext.InventoryItems
            .Where(item => productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        foreach (var line in reservation.Lines)
        {
            if (inventory.TryGetValue(line.ProductId, out var item))
            {
                var release = item.Release(line.Quantity, now);
                if (release.IsFailure)
                {
                    throw new InventoryWorkflowException(release.Error.Code);
                }
            }
        }

        var expiration = reservation.Expire(now);
        if (expiration.IsFailure)
        {
            throw new InventoryWorkflowException(expiration.Error.Code);
        }
    }
}
