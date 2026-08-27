using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Order.Api.Persistence;

namespace Order.Api.Integration;

internal sealed class OrderReferenceDataHealthCheck(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
    : IHealthCheck
{
    private static readonly TimeSpan MaximumReconciliationAge = TimeSpan.FromHours(12);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var state = await dbContext.Set<OrderReferenceDataSynchronization>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == OrderReferenceDataSynchronization.SingletonId,
                cancellationToken);

        if (state?.ReadyAt is null || state.LastCompletedAt is null)
        {
            return HealthCheckResult.Unhealthy(
                "Order reference-data projections have not completed initial synchronization.");
        }

        var age = timeProvider.GetUtcNow() - state.LastCompletedAt.Value;
        return age <= MaximumReconciliationAge
            ? HealthCheckResult.Healthy(
                "Order reference-data projections are synchronized.")
            : HealthCheckResult.Degraded(
                "Order reference-data projections have not reconciled within the expected window.");
    }
}
