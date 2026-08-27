using Inventory.Api.Domain;
using Inventory.Api.Persistence;
using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Integration;

internal sealed class ReserveInventoryConsumer(
    InventoryDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider) : IConsumer<ReserveInventory>
{
    public async Task Consume(ConsumeContext<ReserveInventory> context)
    {
        var message = context.Message;
        if (message.OrderId == Guid.Empty || message.Items.Count == 0 ||
            message.Items.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0) ||
            message.Items.Select(item => item.ProductId).Distinct().Count() != message.Items.Count)
        {
            throw new InventoryWorkflowException("inventory.invalid_reserve_command");
        }

        var existing = await dbContext.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrderId == message.OrderId, context.CancellationToken);
        if (existing is not null)
        {
            return;
        }

        var productIds = message.Items.Select(item => item.ProductId).ToArray();
        var inventory = await dbContext.InventoryItems
            .Where(item => productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, context.CancellationToken);
        var canReserve = inventory.Count == productIds.Length && message.Items.All(item => inventory[item.ProductId].Available >= item.Quantity);
        var now = timeProvider.GetUtcNow();

        if (!canReserve)
        {
            dbContext.Reservations.Add(InventoryReservation.CreateRejected(message.OrderId, "insufficient_stock", message.ExpiresAtUtc, now));
            await eventPublisher.PublishAsync(
                new InventoryRejected(message.OrderId, "insufficient_stock", now),
                new IntegrationMessageMetadata(CorrelationId: message.OrderId),
                context.CancellationToken);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var reservation = InventoryReservation.CreateActive(
            message.OrderId,
            message.Items.Select(item => (item.ProductId, item.Quantity)).ToArray(),
            message.ExpiresAtUtc,
            now);
        if (reservation.IsFailure)
        {
            throw new InventoryWorkflowException(reservation.Error.Code);
        }

        foreach (var requested in message.Items)
        {
            var reserved = inventory[requested.ProductId].Reserve(requested.Quantity, now);
            if (reserved.IsFailure)
            {
                throw new InventoryWorkflowException(reserved.Error.Code);
            }
        }

        dbContext.Reservations.Add(reservation.Value);
        await eventPublisher.PublishAsync(
            new InventoryReserved(message.OrderId, reservation.Value.Id, reservation.Value.ExpiresAt, now),
            new IntegrationMessageMetadata(CorrelationId: message.OrderId),
            context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
