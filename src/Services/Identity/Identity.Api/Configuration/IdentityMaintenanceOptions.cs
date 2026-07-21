using Microsoft.Extensions.Options;

namespace Identity.Api.Configuration;

public sealed class IdentityMaintenanceOptions
{
    public const string SectionName = "IdentityMaintenance";

    public TimeSpan PruneInterval { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan FailureRetryInterval { get; init; } = TimeSpan.FromMinutes(1);
}

internal sealed class IdentityMaintenanceOptionsValidator
    : IValidateOptions<IdentityMaintenanceOptions>
{
    private static readonly TimeSpan MinimumPruneInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumPruneInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan MinimumRecordAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaximumRecordAge = TimeSpan.FromDays(90);
    private static readonly TimeSpan MinimumFailureRetryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumFailureRetryInterval = TimeSpan.FromMinutes(15);

    public ValidateOptionsResult Validate(
        string? name,
        IdentityMaintenanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateRange(
            options.PruneInterval,
            nameof(options.PruneInterval),
            MinimumPruneInterval,
            MaximumPruneInterval,
            failures);
        ValidateRange(
            options.MinimumAge,
            nameof(options.MinimumAge),
            MinimumRecordAge,
            MaximumRecordAge,
            failures);
        ValidateRange(
            options.FailureRetryInterval,
            nameof(options.FailureRetryInterval),
            MinimumFailureRetryInterval,
            MaximumFailureRetryInterval,
            failures);

        if (options.FailureRetryInterval > options.PruneInterval)
        {
            failures.Add(
                $"{Section(nameof(options.FailureRetryInterval))} must not exceed " +
                $"{Section(nameof(options.PruneInterval))}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRange(
        TimeSpan value,
        string propertyName,
        TimeSpan minimum,
        TimeSpan maximum,
        List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"{Section(propertyName)} must be between {minimum} and {maximum}.");
        }
    }

    private static string Section(string propertyName) =>
        $"'{IdentityMaintenanceOptions.SectionName}:{propertyName}'";
}
