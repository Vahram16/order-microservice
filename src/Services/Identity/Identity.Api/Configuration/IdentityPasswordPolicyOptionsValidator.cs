using Microsoft.Extensions.Options;

namespace Identity.Api.Configuration;

internal sealed class IdentityPasswordPolicyOptionsValidator(
    IHostEnvironment environment)
    : IValidateOptions<IdentityPasswordPolicyOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IdentityPasswordPolicyOptions options)
    {
        var failures = new List<string>();

        if (options.MinimumBlocklistEntries is < 1_000 or > 10_000_000)
        {
            failures.Add(
                "'IdentityPasswordPolicy:MinimumBlocklistEntries' must be between 1,000 and 10,000,000.");
        }

        if (string.IsNullOrWhiteSpace(options.BlocklistPath))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add(
                    "'IdentityPasswordPolicy:BlocklistPath' is required outside Development.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        if (!Path.IsPathFullyQualified(options.BlocklistPath))
        {
            failures.Add(
                "'IdentityPasswordPolicy:BlocklistPath' must be an absolute file path.");
        }
        else if (!File.Exists(options.BlocklistPath))
        {
            failures.Add(
                "The configured identity password blocklist file does not exist.");
        }
        else
        {
            try
            {
                var count = File.ReadLines(options.BlocklistPath)
                    .Select(line => line.Trim())
                    .Where(line => line.Length != 0 && !line.StartsWith('#'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(options.MinimumBlocklistEntries)
                    .Count();

                if (!environment.IsDevelopment() &&
                    count < options.MinimumBlocklistEntries)
                {
                    failures.Add(
                        $"The production password blocklist must contain at least {options.MinimumBlocklistEntries:N0} distinct entries.");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(
                    "The configured identity password blocklist file cannot be read.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
