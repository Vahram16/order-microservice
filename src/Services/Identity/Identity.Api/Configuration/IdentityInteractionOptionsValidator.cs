using Microsoft.Extensions.Options;

namespace Identity.Api.Configuration;

internal sealed class IdentityInteractionOptionsValidator(
    IHostEnvironment environment,
    IOptions<AuthorizationServerOptions> authorizationServerOptions)
    : IValidateOptions<IdentityInteractionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IdentityInteractionOptions options)
    {
        var failures = new List<string>();

        Uri? interactionOrigin = null;
        if (!Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin) ||
            string.IsNullOrWhiteSpace(origin.Host) ||
            (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.PathAndQuery.Trim('/')) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            failures.Add(
                "'IdentityInteraction:PublicOrigin' must be an absolute HTTP(S) origin URI.");
        }
        else
        {
            interactionOrigin = origin;
            if (!environment.IsDevelopment() && origin.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add(
                    "'IdentityInteraction:PublicOrigin' must use HTTPS outside Development.");
            }
        }

        ValidateLocalPath(options.LoginPath, nameof(options.LoginPath), failures);
        ValidateLocalPath(options.LogoutPath, nameof(options.LogoutPath), failures);
        ValidateLocalPath(options.AccessDeniedPath, nameof(options.AccessDeniedPath), failures);

        if (options.InteractionTokenLifetime < TimeSpan.FromMinutes(1) ||
            options.InteractionTokenLifetime > TimeSpan.FromMinutes(10))
        {
            failures.Add(
                "'IdentityInteraction:InteractionTokenLifetime' must be between one and ten minutes.");
        }

        if (!environment.IsDevelopment() &&
            interactionOrigin is not null &&
            Uri.TryCreate(
                authorizationServerOptions.Value.Issuer,
                UriKind.Absolute,
                out var issuer) &&
            !string.Equals(
                interactionOrigin.GetLeftPart(UriPartial.Authority),
                issuer.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                "The production identity interaction UI must share the authorization-server origin. Route the UI and API behind the same public origin.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateLocalPath(
        string value,
        string name,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith('/', StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains('?', StringComparison.Ordinal) ||
            value.Contains('#', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal))
        {
            failures.Add(
                $"'IdentityInteraction:{name}' must be a local absolute path without a query or fragment.");
        }
    }
}
