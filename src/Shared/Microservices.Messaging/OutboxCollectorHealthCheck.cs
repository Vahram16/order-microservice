using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microservices.Messaging;

internal sealed class OutboxCollectorHealthCheck<TDbContext>(
    OutboxMetricsCollector<TDbContext> collector) : IHealthCheck
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = collector.IsHealthy
            ? HealthCheckResult.Healthy("The outbox metrics collector completed its latest query successfully.")
            : HealthCheckResult.Unhealthy(
                "The outbox metrics collector has not completed successfully or its latest query failed. " +
                "Last-known backlog values may be stale; backlog size itself does not control readiness.");

        return Task.FromResult(result);
    }
}
