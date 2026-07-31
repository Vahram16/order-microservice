using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Microservices.ServiceDefaults;

public static class ServiceHealthCheckTags
{
    public const string Liveness = "live";
    public const string Readiness = "ready";
}

internal sealed class ServiceReadinessHealthCheck : IHealthCheck, IHostedService
{
    private const string NotReadyDescription = "The service is not accepting traffic.";
    private int _isReady;

    public ServiceReadinessHealthCheck(IHostApplicationLifetime applicationLifetime)
    {
        applicationLifetime.ApplicationStarted.Register(MarkReady);
        applicationLifetime.ApplicationStopping.Register(MarkNotReady);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        MarkNotReady();
        return Task.CompletedTask;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = Volatile.Read(ref _isReady) == 1
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy(NotReadyDescription);

        return Task.FromResult(result);
    }

    private void MarkNotReady() => Volatile.Write(ref _isReady, 0);

    private void MarkReady() => Volatile.Write(ref _isReady, 1);
}
