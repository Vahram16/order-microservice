using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microservices.Security.Tests;

public sealed class DefaultEndpointAuthorizationTests
{
    [Fact]
    public async Task HealthEndpointsExplicitlyAllowAnonymousProbes()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = string.Empty
        });
        builder.AddWebApiDefaults();

        await using var app = builder.Build();
        app.MapDefaultEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText is "/health" or "/alive")
            .ToArray();

        Assert.Equal(2, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
    }
}
