using Microservices.Application.Messaging;
using Microservices.Contracts.Customers.V1;
using Microservices.Contracts.Products.V1;
using Microservices.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class OrderReferenceDataSynchronizationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OrderReferenceDataSynchronizationWorker> logger)
    : BackgroundService
{
    private const int PageSize = 200;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestRetryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureSynchronizationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Order reference-data synchronization iteration failed.");
            }

            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task EnsureSynchronizationAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var customerSender = scope.ServiceProvider.GetRequiredService<
            IIntegrationCommandSender<SynchronizeCustomerIdentitySnapshot>>();
        var productSender = scope.ServiceProvider.GetRequiredService<
            IIntegrationCommandSender<SynchronizeProductCatalogSnapshot>>();
        var now = timeProvider.GetUtcNow();
        var state = await dbContext.Set<OrderReferenceDataSynchronization>()
            .SingleOrDefaultAsync(
                item => item.Id == OrderReferenceDataSynchronization.SingletonId,
                cancellationToken);

        if (state is null)
        {
            state = OrderReferenceDataSynchronization.Start(Guid.NewGuid(), now);
            dbContext.Add(state);
            await SendCustomerRequestAsync(state, customerSender, cancellationToken);
            await SendProductRequestAsync(state, productSender, cancellationToken);
            await SaveInitialStateAsync(dbContext, cancellationToken);
            return;
        }

        if (state.CustomerCompleted &&
            state.ProductCompleted &&
            state.LastCompletedAt is { } completedAt &&
            now - completedAt >= ReconciliationInterval)
        {
            state.BeginCycle(Guid.NewGuid(), now);
            await SendCustomerRequestAsync(state, customerSender, cancellationToken);
            await SendProductRequestAsync(state, productSender, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var changed = false;
        if (!state.CustomerCompleted &&
            now - state.CustomerLastRequestedAt >= RequestRetryInterval)
        {
            state.MarkCustomerRequested(now);
            await SendCustomerRequestAsync(state, customerSender, cancellationToken);
            changed = true;
        }

        if (!state.ProductCompleted &&
            now - state.ProductLastRequestedAt >= RequestRetryInterval)
        {
            state.MarkProductRequested(now);
            await SendProductRequestAsync(state, productSender, cancellationToken);
            changed = true;
        }

        if (!changed)
        {
            return;
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

    private static async Task SaveInitialStateAsync(
        OrderDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(
                OrderDatabaseConstraints.ReferenceDataSynchronizationPrimaryKey))
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static Task SendCustomerRequestAsync(
        OrderReferenceDataSynchronization state,
        IIntegrationCommandSender<SynchronizeCustomerIdentitySnapshot> sender,
        CancellationToken cancellationToken) =>
        sender.SendAsync(
            new SynchronizeCustomerIdentitySnapshot(
                state.SnapshotId,
                state.CustomerAfterCustomerId,
                PageSize),
            new IntegrationMessageMetadata(CorrelationId: state.SnapshotId),
            cancellationToken);

    private static Task SendProductRequestAsync(
        OrderReferenceDataSynchronization state,
        IIntegrationCommandSender<SynchronizeProductCatalogSnapshot> sender,
        CancellationToken cancellationToken) =>
        sender.SendAsync(
            new SynchronizeProductCatalogSnapshot(
                state.SnapshotId,
                state.ProductAfterProductId,
                PageSize),
            new IntegrationMessageMetadata(CorrelationId: state.SnapshotId),
            cancellationToken);
}
