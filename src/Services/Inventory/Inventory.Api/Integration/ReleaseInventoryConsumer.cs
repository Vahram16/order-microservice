using Inventory.Api.Domain;
using Inventory.Api.Persistence;
using MassTransit;
using Microservices.Contracts.Inventory.V1;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Integration;

internal sealed class ReleaseInventoryConsumer(InventoryDbContext dbContext, TimeProvider timeProvider) : IConsumer<ReleaseInventory>
{
    public async Task Consume(ConsumeContext<ReleaseInventory> context)
    {
        var reservation = await dbContext.Reservations.Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == context.Message.ReservationId && item.OrderId == context.Message.OrderId, context.CancellationToken)
            ?? throw new InventoryWorkflowException("inventory.reservation_not_found");
        if (reservation.Status is InventoryReservationStatus.Released or InventoryReservationStatus.Expired or InventoryReservationStatus.Rejected) return;

        var productIds = reservation.Lines.Select(item => item.ProductId).ToArray();
        var inventory = await dbContext.InventoryItems.Where(item => productIds.Contains(item.ProductId)).ToDictionaryAsync(item => item.ProductId, context.CancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var line in reservation.Lines)
        {
            if (!inventory.TryGetValue(line.ProductId, out var item)) throw new InventoryWorkflowException("inventory.reservation_item_missing");
            var release = reservation.Status == InventoryReservationStatus.Committed
                ? item.RestoreCommitted(line.Quantity, now)
                : item.Release(line.Quantity, now);
            if (release.IsFailure) throw new InventoryWorkflowException(release.Error.Code);
        }

        var transition = reservation.Status == InventoryReservationStatus.Committed
            ? reservation.CompensateCommit(now)
            : reservation.Release(now);
        if (transition.IsFailure) throw new InventoryWorkflowException(transition.Error.Code);
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
