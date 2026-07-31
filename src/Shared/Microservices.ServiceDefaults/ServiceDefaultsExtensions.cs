using System.Globalization;
using Microservices.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class ServiceDefaultsExtensions
{
    private const string HealthPath = "/health";
    private const string AlivePath = "/alive";
    private const string GenericOtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string TracesOtlpEndpointKey = "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT";
    private const string MetricsOtlpEndpointKey = "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT";
    private const string LogsOtlpEndpointKey = "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT";
    private const string TracesSamplerKey = "OTEL_TRACES_SAMPLER";
    private const string LogsSignalName = "LOGS";
    private const string MetricsSignalName = "METRICS";
    private const string TracesSignalName = "TRACES";

    private sealed class HostDefaultsMarker;

    private sealed class WebApiDefaultsMarker;

    /// <summary>
    /// Adds the host-level defaults shared by APIs, workers, and one-shot jobs.
    /// </summary>
    public static TBuilder AddHostDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(service =>
                service.ServiceType == typeof(HostDefaultsMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton(new HostDefaultsMarker());
        AddServiceDefaultsOptions(builder);

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(options =>
                options.Retry.DisableForUnsafeHttpMethods());
            http.AddServiceDiscovery();
        });

        var useCrossCuttingOtlpExporter =
            HasConfiguredValue(builder.Configuration, GenericOtlpEndpointKey);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            if (!useCrossCuttingOtlpExporter &&
                IsOtlpExporterConfigured(builder.Configuration, LogsOtlpEndpointKey))
            {
                logging.AddOtlpExporter(options =>
                    ConfigureSignalSpecificOtlpExporter(
                        options,
                        builder.Configuration,
                        LogsSignalName));
            }
        });
        builder.Services.AddOptions<OpenTelemetryLoggerOptions>()
            .Configure<IOptions<ServiceDefaultsOptions>>((logging, configured) =>
            {
                logging.IncludeFormattedMessage =
                    configured.Value.Telemetry.IncludeFormattedLogMessage;
                logging.IncludeScopes = configured.Value.Telemetry.IncludeLogScopes;
            });

        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"];
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            serviceName = builder.Environment.ApplicationName;
        }

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!useCrossCuttingOtlpExporter &&
                    IsOtlpExporterConfigured(
                        builder.Configuration,
                        MetricsOtlpEndpointKey))
                {
                    metrics.AddOtlpExporter(options =>
                        ConfigureSignalSpecificOtlpExporter(
                            options,
                            builder.Configuration,
                            MetricsSignalName));
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddHttpClientInstrumentation();

                if (string.IsNullOrWhiteSpace(builder.Configuration[TracesSamplerKey]))
                {
                    var useDevelopmentFullSampling =
                        builder.Environment.IsDevelopment() &&
                        builder.Configuration[
                            $"{ServiceDefaultsOptions.SectionName}:Telemetry:{nameof(ServiceTelemetryOptions.TraceSamplingRatio)}"] is null;

                    tracing.SetSampler(services =>
                    {
                        var configured = services
                            .GetRequiredService<IOptions<ServiceDefaultsOptions>>()
                            .Value;
                        var samplingRatio = useDevelopmentFullSampling
                            ? 1
                            : configured.Telemetry.TraceSamplingRatio;

                        return new ParentBasedSampler(
                            new TraceIdRatioBasedSampler(samplingRatio));
                    });
                }

                if (!useCrossCuttingOtlpExporter &&
                    IsOtlpExporterConfigured(
                        builder.Configuration,
                        TracesOtlpEndpointKey))
                {
                    tracing.AddOtlpExporter(options =>
                        ConfigureSignalSpecificOtlpExporter(
                            options,
                            builder.Configuration,
                            TracesSignalName));
                }
            });

        if (useCrossCuttingOtlpExporter)
        {
            // The cross-cutting registration is the OTel 1.17 path that honors
            // generic settings together with all signal-specific overrides.
            openTelemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Adds defaults for an HTTP API, including host defaults, reverse-proxy trust,
    /// ASP.NET Core telemetry, and platform health checks.
    /// </summary>
    public static WebApplicationBuilder AddWebApiDefaults(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddWebDefaults(builder);

        return builder;
    }

    private static TBuilder AddWebDefaults<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddHostDefaults();

        if (builder.Services.Any(service =>
                service.ServiceType == typeof(WebApiDefaultsMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton(new WebApiDefaultsMarker());
        AddReverseProxyDefaults(builder);

        builder.Services.AddSingleton<ServiceReadinessHealthCheck>();
        builder.Services.AddHostedService(services =>
            services.GetRequiredService<ServiceReadinessHealthCheck>());
        builder.Services.AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy(),
                [ServiceHealthCheckTags.Liveness])
            .AddCheck<ServiceReadinessHealthCheck>(
                "service-readiness",
                tags: [ServiceHealthCheckTags.Readiness]);

        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.AddAspNetCoreInstrumentation());
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddAspNetCoreInstrumentation(options =>
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments(HealthPath) &&
                    !context.Request.Path.StartsWithSegments(AlivePath)));

        return builder;
    }

    /// <summary>
    /// Adds defaults for a one-shot process whose host is explicitly started and stopped.
    /// </summary>
    public static TBuilder AddJobDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder => builder.AddHostDefaults();

    // Compatibility overloads retain the conventional Aspire-style entry point while
    // allowing new callers to state their process profile explicitly.
    public static WebApplicationBuilder AddServiceDefaults(
        this WebApplicationBuilder builder) => builder.AddWebApiDefaults();

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
        => AddWebDefaults(builder);

    private static void AddServiceDefaultsOptions<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<IValidateOptions<ServiceDefaultsOptions>,
            ServiceDefaultsOptionsValidator>();
        builder.Services.AddOptions<ServiceDefaultsOptions>()
            .Bind(builder.Configuration.GetSection(ServiceDefaultsOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddOptions<HostOptions>()
            .Configure<IOptions<ServiceDefaultsOptions>>((host, configured) =>
                host.ShutdownTimeout = configured.Value.ShutdownTimeout);
    }

    private static void AddReverseProxyDefaults<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<IValidateOptions<ReverseProxyOptions>,
            ReverseProxyOptionsValidator>();
        builder.Services.AddOptions<ReverseProxyOptions>()
            .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((forwarded, configured) =>
                ReverseProxyOptionsSetup.Configure(forwarded, configured.Value));
    }

    public static WebApplication UseConfiguredForwardedHeaders(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.Enabled)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthPath, new HealthCheckOptions
        {
            Predicate = registration =>
                registration.Tags.Contains(ServiceHealthCheckTags.Readiness)
        }).AllowAnonymous();
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = registration =>
                registration.Tags.Contains(ServiceHealthCheckTags.Liveness)
        }).AllowAnonymous();

        return app;
    }

    internal static bool IsOtlpExporterConfigured(
        IConfiguration configuration,
        string signalEndpointKey) =>
        HasConfiguredValue(configuration, GenericOtlpEndpointKey) ||
        HasConfiguredValue(configuration, signalEndpointKey);

    internal static void ConfigureSignalSpecificOtlpExporter(
        OtlpExporterOptions options,
        IConfiguration configuration,
        string signalName)
    {
        var endpointKey = $"OTEL_EXPORTER_OTLP_{signalName}_ENDPOINT";
        var endpointValue = configuration[endpointKey];
        if (!Uri.TryCreate(endpointValue?.Trim(), UriKind.Absolute, out var endpoint) ||
            !(endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw InvalidOtlpConfiguration(
                signalName,
                $"'{endpointKey}' must be an absolute HTTP or HTTPS URI.");
        }

        options.Endpoint = endpoint;

        var protocolKey = $"OTEL_EXPORTER_OTLP_{signalName}_PROTOCOL";
        var protocolValue = configuration[protocolKey];
        if (!string.IsNullOrWhiteSpace(protocolValue))
        {
            options.Protocol = protocolValue.Trim().ToLowerInvariant() switch
            {
                "grpc" => OtlpExportProtocol.Grpc,
                "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                _ => throw InvalidOtlpConfiguration(
                    signalName,
                    $"'{protocolKey}' must be 'grpc' or 'http/protobuf'.")
            };
        }

        var headersValue = configuration[$"OTEL_EXPORTER_OTLP_{signalName}_HEADERS"];
        if (!string.IsNullOrWhiteSpace(headersValue))
        {
            options.Headers = headersValue;
        }

        var timeoutKey = $"OTEL_EXPORTER_OTLP_{signalName}_TIMEOUT";
        var timeoutValue = configuration[timeoutKey];
        if (!string.IsNullOrWhiteSpace(timeoutValue))
        {
            if (!int.TryParse(
                    timeoutValue.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var timeoutMilliseconds) ||
                timeoutMilliseconds <= 0)
            {
                throw InvalidOtlpConfiguration(
                    signalName,
                    $"'{timeoutKey}' must be a positive integer number of milliseconds.");
            }

            options.TimeoutMilliseconds = timeoutMilliseconds;
        }

        var compressionKey = $"OTEL_EXPORTER_OTLP_{signalName}_COMPRESSION";
        var compressionValue = configuration[compressionKey];
        if (!string.IsNullOrWhiteSpace(compressionValue))
        {
            options.Compression = compressionValue.Trim().ToLowerInvariant() switch
            {
                "none" => OtlpExportCompression.None,
                "gzip" => OtlpExportCompression.GZip,
                _ => throw InvalidOtlpConfiguration(
                    signalName,
                    $"'{compressionKey}' must be 'none' or 'gzip'.")
            };
        }
    }

    private static bool HasConfiguredValue(
        IConfiguration configuration,
        string key) => !string.IsNullOrWhiteSpace(configuration[key]);

    private static OptionsValidationException InvalidOtlpConfiguration(
        string signalName,
        string failure) => new(
        signalName,
        typeof(OtlpExporterOptions),
        [failure]);
}
