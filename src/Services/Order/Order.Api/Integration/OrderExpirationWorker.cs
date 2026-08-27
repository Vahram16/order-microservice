using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Orders.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class OrderExpirationWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireDueOrdersAsync(stoppingToken);
            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task ExpireDueOrdersAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var releaseSender = scope.ServiceProvider.GetRequiredService<IIntegrationCommandSender<ReleaseInventory>>();
        var cancelPaymentSender = scope.ServiceProvider.GetRequiredService<IIntegrationCommandSender<CancelOrderPayment>>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var now = timeProvider.GetUtcNow();
        var due = await dbContext.Orders.Where(order => order.ExpiresAt <= now && order.Status != OrderStatus.PaymentAuthorized && order.Status != OrderStatus.PaymentCapturing && order.Status != OrderStatus.Confirmed && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Expired)
            .OrderBy(order => order.ExpiresAt).Take(20).ToListAsync(cancellationToken);
        foreach (var order in due)
        {
            var expiration = order.Expire(now); if (expiration.IsFailure) continue;
            if (order.InventoryReservationId is { } reservationId)
                await releaseSender.SendAsync(new ReleaseInventory(order.Id, reservationId, "checkout_expired"), new IntegrationMessageMetadata(CorrelationId: order.Id), cancellationToken);
            if (order.PaymentAttemptId is { } paymentAttemptId)
                await cancelPaymentSender.SendAsync(new CancelOrderPayment(order.Id, paymentAttemptId, "checkout_expired"), new IntegrationMessageMetadata(CorrelationId: order.Id), cancellationToken);
            await eventPublisher.PublishAsync(new OrderExpired(order.Id, order.CustomerId, now), new IntegrationMessageMetadata(CorrelationId: order.Id), cancellationToken);
        }
        if (due.Count == 0) return;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { dbContext.ChangeTracker.Clear(); }
    }
}
