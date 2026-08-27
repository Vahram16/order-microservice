using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class OrderReferenceDataHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var ready = await dbContext.Set<OrderReferenceDataSynchronization>().AsNoTracking().AnyAsync(item => item.Id == OrderReferenceDataSynchronization.SingletonId && item.ReadyAt != null, cancellationToken);
        return ready ? HealthCheckResult.Healthy("Order reference-data projections are synchronized.") : HealthCheckResult.Unhealthy("Order reference-data projections are still synchronizing.");
    }
}
