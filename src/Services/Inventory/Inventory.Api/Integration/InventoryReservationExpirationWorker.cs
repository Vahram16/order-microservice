using Inventory.Api.Domain;
using Inventory.Api.Persistence;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Integration;

internal sealed class InventoryReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireDueReservationsAsync(stoppingToken);
            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task ExpireDueReservationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var now = timeProvider.GetUtcNow();
        var reservations = await dbContext.Reservations
            .Include(item => item.Lines)
            .Where(item => item.Status == InventoryReservationStatus.Active && item.ExpiresAt <= now)
            .OrderBy(item => item.ExpiresAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        if (reservations.Count == 0)
        {
            return;
        }

        var productIds = reservations.SelectMany(item => item.Lines).Select(item => item.ProductId).Distinct().ToArray();
        var inventory = await dbContext.InventoryItems
            .Where(item => productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);

        foreach (var reservation in reservations)
        {
            foreach (var line in reservation.Lines)
            {
                if (!inventory.TryGetValue(line.ProductId, out var item))
                {
                    throw new InventoryWorkflowException("inventory.reservation_item_missing");
                }

                var release = item.Release(line.Quantity, now);
                if (release.IsFailure)
                {
                    throw new InventoryWorkflowException(release.Error.Code);
                }
            }

            var expiration = reservation.Expire(now);
            if (expiration.IsFailure)
            {
                continue;
            }

            await publisher.PublishAsync(
                new InventoryReservationExpired(reservation.OrderId, reservation.Id, now),
                new IntegrationMessageMetadata(CorrelationId: reservation.OrderId),
                cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
