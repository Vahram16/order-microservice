using Microservices.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Microservices.ServiceDefaults.Tests;

public sealed class ServiceDefaultsOptionsTests
{
    [Fact]
    public void DefaultsAreProductionSafe()
    {
        var options = new ServiceDefaultsOptions();
        var result = new ServiceDefaultsOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ShutdownTimeout);
        Assert.False(options.Telemetry.IncludeFormattedLogMessage);
        Assert.False(options.Telemetry.IncludeLogScopes);
        Assert.Equal(0.1, options.Telemetry.TraceSamplingRatio);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InvalidTraceSamplingRatioFailsValidation(double ratio)
    {
        var result = new ServiceDefaultsOptionsValidator().Validate(
            null,
            new ServiceDefaultsOptions
            {
                Telemetry = new ServiceTelemetryOptions
                {
                    TraceSamplingRatio = ratio
                }
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void InvalidConfigurationFailsWhenAJobHostIsBuilt()
    {
        var builder = Host.CreateApplicationBuilder();
        DisableOtlpExport(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceDefaults:Telemetry:TraceSamplingRatio"] = "2"
        });
        builder.AddJobDefaults();

        Assert.Throws<OptionsValidationException>(() => builder.Build());
    }

    [Fact]
    public void ConfiguredPoliciesFlowIntoHostAndLoggingOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        DisableOtlpExport(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceDefaults:ShutdownTimeout"] = "00:00:45",
            ["ServiceDefaults:Telemetry:IncludeFormattedLogMessage"] = "true",
            ["ServiceDefaults:Telemetry:IncludeLogScopes"] = "true"
        });
        builder.AddJobDefaults();
        using var host = builder.Build();

        var hostOptions = host.Services.GetRequiredService<IOptions<HostOptions>>().Value;
        var loggingOptions = host.Services
            .GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>()
            .Value;

        Assert.Equal(TimeSpan.FromSeconds(45), hostOptions.ShutdownTimeout);
        Assert.True(loggingOptions.IncludeFormattedMessage);
        Assert.True(loggingOptions.IncludeScopes);
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
}
