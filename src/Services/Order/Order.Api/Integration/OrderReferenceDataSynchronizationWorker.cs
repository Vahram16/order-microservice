using Microservices.Application.Messaging;
using Microservices.Contracts.Customers.V1;
using Microservices.Contracts.Products.V1;
using Microservices.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class OrderReferenceDataSynchronizationWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : BackgroundService
{
    private const int PageSize = 200;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EnsureSynchronizationAsync(stoppingToken);
            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task EnsureSynchronizationAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var customerSender = scope.ServiceProvider.GetRequiredService<IIntegrationCommandSender<SynchronizeCustomerIdentitySnapshot>>();
        var productSender = scope.ServiceProvider.GetRequiredService<IIntegrationCommandSender<SynchronizeProductCatalogSnapshot>>();
        var now = timeProvider.GetUtcNow();
        var state = await dbContext.Set<OrderReferenceDataSynchronization>().SingleOrDefaultAsync(item => item.Id == OrderReferenceDataSynchronization.SingletonId, cancellationToken);

        if (state is null)
        {
            state = OrderReferenceDataSynchronization.Start(Guid.NewGuid(), now);
            dbContext.Add(state);
            await SendRequestsAsync(state, customerSender, productSender, cancellationToken);
        }
        else if (state.CycleCompletedAt is null && now - state.LastRequestedAt >= RequestRetryInterval)
        {
            state.MarkRequested(now);
            await SendRequestsAsync(state, customerSender, productSender, cancellationToken);
        }
        else if (state.CycleCompletedAt is { } completedAt && now - completedAt >= ReconciliationInterval)
        {
            state.BeginCycle(Guid.NewGuid(), now);
            await SendRequestsAsync(state, customerSender, productSender, cancellationToken);
        }
        else
        {
            return;
        }

        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { dbContext.ChangeTracker.Clear(); }
        catch (DbUpdateException exception) when (exception.IsUniqueConstraintViolation(OrderDatabaseConstraints.ReferenceDataSynchronizationPrimaryKey)) { dbContext.ChangeTracker.Clear(); }
    }

    private static async Task SendRequestsAsync(
        OrderReferenceDataSynchronization state,
        IIntegrationCommandSender<SynchronizeCustomerIdentitySnapshot> customerSender,
        IIntegrationCommandSender<SynchronizeProductCatalogSnapshot> productSender,
        CancellationToken cancellationToken)
    {
        var metadata = new IntegrationMessageMetadata(CorrelationId: state.SnapshotId);
        if (!state.CustomerCompleted)
            await customerSender.SendAsync(new SynchronizeCustomerIdentitySnapshot(state.SnapshotId, null, PageSize), metadata, cancellationToken);
        if (!state.ProductCompleted)
            await productSender.SendAsync(new SynchronizeProductCatalogSnapshot(state.SnapshotId, null, PageSize), metadata, cancellationToken);
    }
}
