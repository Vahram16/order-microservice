using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microservices.Security;

internal sealed class ApiSecurityOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<ApiSecurityOptions>
{
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    public ValidateOptionsResult Validate(string? name, ApiSecurityOptions options)
    {
        var failures = new List<string>();

        if (!environment.IsDevelopment() && !options.RequireHttpsMetadata)
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.RequireHttpsMetadata)} " +
                "must be true outside Development.");
        }

        ValidateEndpoint(
            options.Authority,
            nameof(options.Authority),
            required: true,
            options.RequireHttpsMetadata,
            failures);
        ValidateEndpoint(
            options.MetadataAddress,
            nameof(options.MetadataAddress),
            required: false,
            options.RequireHttpsMetadata,
            failures);

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.Audience)} is required.");
        }
        else if (options.Audience.Any(char.IsWhiteSpace))
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.Audience)} " +
                "must not contain whitespace.");
        }

        if (options.ClockSkew < TimeSpan.Zero || options.ClockSkew > MaximumClockSkew)
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.ClockSkew)} " +
                $"must be between zero and {MaximumClockSkew}.");
        }

        if (options.ValidTokenTypes is null || options.ValidTokenTypes.Length == 0)
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.ValidTokenTypes)} " +
                "must contain at least one access-token type.");
        }
        else if (options.ValidTokenTypes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.ValidTokenTypes)} " +
                "must not contain empty values.");
        }
        else if (options.ValidTokenTypes.Distinct(StringComparer.Ordinal).Count() !=
                 options.ValidTokenTypes.Length)
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{nameof(options.ValidTokenTypes)} " +
                "must not contain duplicate values.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateEndpoint(
        string? value,
        string propertyName,
        bool required,
        bool requireHttps,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                failures.Add($"{ApiSecurityOptions.SectionName}:{propertyName} is required.");
            }

            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{propertyName} " +
                "must be an absolute HTTP or HTTPS URI.");
            return;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{propertyName} " +
                "must not contain user information, a query, or a fragment.");
        }

        var insecureDevelopmentEndpoint =
            environment.IsDevelopment() && !requireHttps;
        if (uri.Scheme != Uri.UriSchemeHttps && !insecureDevelopmentEndpoint)
        {
            failures.Add(
                $"{ApiSecurityOptions.SectionName}:{propertyName} " +
                "must use HTTPS. HTTP is allowed only when Development explicitly " +
                $"sets {nameof(ApiSecurityOptions.RequireHttpsMetadata)} to false.");
        }
    }
}
