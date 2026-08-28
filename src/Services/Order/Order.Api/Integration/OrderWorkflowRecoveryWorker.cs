using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Payments.V1;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal enum OrderWorkflowRecoveryAction
{
    None = 0,
    CommitInventory = 1,
    CapturePayment = 2
}

internal static class OrderWorkflowRecoveryPolicy
{
    public static OrderWorkflowRecoveryAction GetAction(
        Domain.Order order,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (staleAfter <= TimeSpan.Zero || order.UpdatedAt > now - staleAfter)
        {
            return OrderWorkflowRecoveryAction.None;
        }

        return order.Status switch
        {
            OrderStatus.PaymentAuthorized => OrderWorkflowRecoveryAction.CommitInventory,
            OrderStatus.PaymentCapturing => OrderWorkflowRecoveryAction.CapturePayment,
            _ => OrderWorkflowRecoveryAction.None
        };
    }
}

internal sealed class OrderWorkflowRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OrderWorkflowRecoveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RecoverStuckOrdersAsync(stoppingToken);
            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task RecoverStuckOrdersAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var inventorySender = scope.ServiceProvider.GetRequiredService<IIntegrationCommandSender<CommitInventoryReservation>>();
        var captureSender = scope.ServiceProvider.GetRequiredService<IIntegrationCommandSender<CaptureOrderPayment>>();
        var now = timeProvider.GetUtcNow();
        var cutoff = now - StaleAfter;

        var candidates = await dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                (order.Status == OrderStatus.PaymentAuthorized || order.Status == OrderStatus.PaymentCapturing) &&
                order.UpdatedAt <= cutoff)
            .OrderBy(order => order.UpdatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var order in candidates)
        {
            switch (OrderWorkflowRecoveryPolicy.GetAction(order, now, StaleAfter))
            {
                case OrderWorkflowRecoveryAction.CommitInventory when order.InventoryReservationId is { } reservationId:
                    await inventorySender.SendAsync(
                        new CommitInventoryReservation(order.Id, reservationId),
                        new IntegrationMessageMetadata(CorrelationId: order.Id),
                        cancellationToken);
                    logger.LogWarning(
                        "Re-driving inventory commit for stale order {OrderId} in state {OrderStatus}.",
                        order.Id,
                        order.Status);
                    queued++;
                    break;

                case OrderWorkflowRecoveryAction.CapturePayment when order.PaymentAttemptId is { } paymentAttemptId:
                    // A fresh transport MessageId is intentional: the Payment consumer re-fetches provider state
                    // and uses provider-level idempotency, so this command repairs a lost workflow outcome.
                    await captureSender.SendAsync(
                        new CaptureOrderPayment(order.Id, paymentAttemptId),
                        new IntegrationMessageMetadata(CorrelationId: order.Id),
                        cancellationToken);
                    logger.LogWarning(
                        "Re-driving payment capture reconciliation for stale order {OrderId} in state {OrderStatus}.",
                        order.Id,
                        order.Status);
                    queued++;
                    break;

                case OrderWorkflowRecoveryAction.CommitInventory:
                case OrderWorkflowRecoveryAction.CapturePayment:
                    logger.LogCritical(
                        "Order {OrderId} is stuck in {OrderStatus} but is missing the workflow identity required for recovery.",
                        order.Id,
                        order.Status);
                    break;
            }
        }

        if (queued > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
