using System.Net;
using System.Net.Http.Headers;
using Microservices.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;

namespace Microservices.ServiceDefaults.Tests;

public sealed class ServiceDefaultsExtensionsTests
{
    [Fact]
    public async Task DefaultHttpResilienceRetriesOnlySafeMethods()
    {
        var builder = Host.CreateApplicationBuilder();
        DisableOtlpExport(builder.Configuration);
        builder.AddJobDefaults();
        var handler = new CountingHandler();
        builder.Services.AddHttpClient("test-client")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();

        var factory = host.Services.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("test-client");

        foreach (var method in new[]
                 {
                     HttpMethod.Post,
                     HttpMethod.Put,
                     HttpMethod.Patch,
                     HttpMethod.Delete,
                     HttpMethod.Connect
                 })
        {
            using var request = new HttpRequestMessage(
                method,
                "https://example.test/");
            using var response = await client.SendAsync(request);

            Assert.Equal(1, handler.GetAttempts(method));
        }

        using var getResponse = await client.GetAsync("https://example.test/");

        Assert.True(handler.GetAttempts(HttpMethod.Get) > 1);
    }

    [Theory]
    [InlineData("/health", ServiceHealthCheckTags.Readiness,
        StatusCodes.Status503ServiceUnavailable)]
    [InlineData("/health", ServiceHealthCheckTags.Liveness,
        StatusCodes.Status200OK)]
    [InlineData("/health", null, StatusCodes.Status200OK)]
    [InlineData("/alive", ServiceHealthCheckTags.Liveness,
        StatusCodes.Status503ServiceUnavailable)]
    [InlineData("/alive", ServiceHealthCheckTags.Readiness,
        StatusCodes.Status200OK)]
    [InlineData("/alive", null, StatusCodes.Status200OK)]
    public async Task DefaultHealthEndpointsSelectOnlyTheirOwnTags(
        string route,
        string? checkTag,
        int expectedStatus)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        DisableOtlpExport(builder.Configuration);
        builder.AddWebApiDefaults();
        var tags = checkTag is null ? [] : new[] { checkTag };
        builder.Services.AddHealthChecks().AddCheck(
            "controlled-failure",
            () => HealthCheckResult.Unhealthy(),
            tags: tags);

        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();

        try
        {
            var endpoint = ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(candidate => candidate.RoutePattern.RawText == route);
            var context = new DefaultHttpContext
            {
                RequestServices = app.Services
            };
            context.Request.Path = route;
            context.Response.Body = Stream.Null;
            context.SetEndpoint(endpoint);

            await endpoint.RequestDelegate!(context);

            Assert.Equal(expectedStatus, context.Response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", true)]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", true)]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", false)]
    [InlineData("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", true)]
    [InlineData("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", true)]
    public void OtlpExporterRegistrationHonorsGenericAndSignalEndpoints(
        string configuredKey,
        string signalKey,
        bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configuredKey] = "http://collector.example.test:4317"
            })
            .Build();

        Assert.Equal(
            expected,
            ServiceDefaultsExtensions.IsOtlpExporterConfigured(
                configuration,
                signalKey));
    }

    [Fact]
    public void BlankOtlpEndpointsDoNotRegisterAnExporter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "  ",
                ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = string.Empty
            })
            .Build();

        Assert.False(ServiceDefaultsExtensions.IsOtlpExporterConfigured(
            configuration,
            "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"));
    }

    [Fact]
    public void SignalSpecificOtlpSettingsConfigureTheEffectiveExporter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] =
                    "https://collector.example.test/v1/traces",
                ["OTEL_EXPORTER_OTLP_TRACES_PROTOCOL"] = "http/protobuf",
                ["OTEL_EXPORTER_OTLP_TRACES_HEADERS"] = "api-key=secret-reference",
                ["OTEL_EXPORTER_OTLP_TRACES_TIMEOUT"] = "5000",
                ["OTEL_EXPORTER_OTLP_TRACES_COMPRESSION"] = "gzip"
            })
            .Build();
        var options = new OtlpExporterOptions();

        ServiceDefaultsExtensions.ConfigureSignalSpecificOtlpExporter(
            options,
            configuration,
            "TRACES");

        Assert.Equal(
            new Uri("https://collector.example.test/v1/traces"),
            options.Endpoint);
        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
        Assert.Equal("api-key=secret-reference", options.Headers);
        Assert.Equal(5000, options.TimeoutMilliseconds);
        Assert.Equal(OtlpExportCompression.GZip, options.Compression);
    }

    [Fact]
    public void InvalidSignalSpecificOtlpEndpointFailsWithoutEchoingItsValue()
    {
        const string invalidEndpoint = "not-a-uri-with-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = invalidEndpoint
            })
            .Build();
        var options = new OtlpExporterOptions();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            ServiceDefaultsExtensions.ConfigureSignalSpecificOtlpExporter(
                options,
                configuration,
                "TRACES"));

        Assert.DoesNotContain(invalidEndpoint, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WebDefaultsAreIdempotent()
    {
        var builder = WebApplication.CreateBuilder();
        DisableOtlpExport(builder.Configuration);

        builder.AddHostDefaults();
        builder.AddWebApiDefaults();
        builder.AddJobDefaults();
        builder.AddServiceDefaults();
        builder.AddWebApiDefaults();

        using var services = builder.Services.BuildServiceProvider();
        var registrations = services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.Single(registrations, registration => registration.Name == "self");
        Assert.Single(
            registrations,
            registration => registration.Name == "service-readiness");
    }

    [Fact]
    public void JobDefaultsExcludeWebOnlyInfrastructure()
    {
        var builder = Host.CreateApplicationBuilder();
        DisableOtlpExport(builder.Configuration);

        builder.AddJobDefaults();

        Assert.DoesNotContain(builder.Services, service =>
            service.ServiceType == typeof(ServiceReadinessHealthCheck));
        Assert.DoesNotContain(builder.Services, service =>
            service.ImplementationType == typeof(ReverseProxyOptionsValidator));
    }

    [Fact]
    public async Task ReadinessTurnsUnhealthyWhenTheHostStartsStopping()
    {
        var builder = Host.CreateApplicationBuilder();
        DisableOtlpExport(builder.Configuration);
        builder.AddServiceDefaults();
        using var host = builder.Build();
        var readiness = host.Services.GetRequiredService<ServiceReadinessHealthCheck>();

        Assert.Equal(
            HealthStatus.Unhealthy,
            (await readiness.CheckHealthAsync(new HealthCheckContext())).Status);

        await host.StartAsync();

        try
        {
            Assert.Equal(
                HealthStatus.Healthy,
                (await readiness.CheckHealthAsync(new HealthCheckContext())).Status);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(
            HealthStatus.Unhealthy,
            (await readiness.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    private static void DisableOtlpExport(ConfigurationManager configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = string.Empty,
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = string.Empty
        });
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Dictionary<HttpMethod, int> _attempts = [];

        public int GetAttempts(HttpMethod method) => _attempts.GetValueOrDefault(method);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _attempts[request.Method] = GetAttempts(request.Method) + 1;

            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);

            return Task.FromResult(response);
        }
    }

}
