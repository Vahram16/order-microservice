using Microsoft.Extensions.Options;

namespace Microservices.ServiceDefaults;

public sealed class ServiceDefaultsOptions
{
    public const string SectionName = "ServiceDefaults";

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public ServiceTelemetryOptions Telemetry { get; init; } = new();
}

public sealed class ServiceTelemetryOptions
{
    public bool IncludeFormattedLogMessage { get; init; }

    public bool IncludeLogScopes { get; init; }

    public double TraceSamplingRatio { get; init; } = 0.1;
}

internal sealed class ServiceDefaultsOptionsValidator : IValidateOptions<ServiceDefaultsOptions>
{
    private static readonly TimeSpan MaximumShutdownTimeout = TimeSpan.FromMinutes(5);

    public ValidateOptionsResult Validate(string? name, ServiceDefaultsOptions options)
    {
        var failures = new List<string>();

        if (options.ShutdownTimeout <= TimeSpan.Zero ||
            options.ShutdownTimeout > MaximumShutdownTimeout)
        {
            failures.Add(
                "'ServiceDefaults:ShutdownTimeout' must be greater than zero and no more than five minutes.");
        }

        if (options.Telemetry is null)
        {
            failures.Add("'ServiceDefaults:Telemetry' must be configured.");
        }
        else if (double.IsNaN(options.Telemetry.TraceSamplingRatio) ||
                 options.Telemetry.TraceSamplingRatio is < 0 or > 1)
        {
            failures.Add(
                "'ServiceDefaults:Telemetry:TraceSamplingRatio' must be between 0 and 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
