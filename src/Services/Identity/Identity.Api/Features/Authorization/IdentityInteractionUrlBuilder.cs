using Identity.Api.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Identity.Api.Features.Authorization;

internal sealed class IdentityInteractionUrlBuilder(
    IOptions<IdentityInteractionOptions> options)
{
    private readonly Uri _origin = new(
        options.Value.PublicOrigin ??
        throw new InvalidOperationException(
            "Identity interaction public origin is not configured."),
        UriKind.Absolute);

    private readonly IdentityInteractionOptions _options = options.Value;

    public string CreateLoginUri(string? returnUrl) =>
        AddQuery(
            _options.LoginPath,
            new Dictionary<string, string?>
            {
                ["returnUrl"] = NormalizeLocalReturnUrl(returnUrl)
            });

    public string CreateAccessDeniedUri(string? returnUrl) =>
        AddQuery(
            _options.AccessDeniedPath,
            new Dictionary<string, string?>
            {
                ["returnUrl"] = NormalizeLocalReturnUrl(returnUrl)
            });

    public string CreateLogoutUri(
        string interactionToken,
        string completionUri) =>
        AddQuery(
            _options.LogoutPath,
            new Dictionary<string, string?>
            {
                ["interactionToken"] = interactionToken,
                ["completionUri"] = NormalizeLocalReturnUrl(completionUri)
            });

    private string AddQuery(
        string path,
        IReadOnlyDictionary<string, string?> parameters)
    {
        var uri = new Uri(_origin, path.TrimStart('/')).ToString();
        return QueryHelpers.AddQueryString(uri, parameters);
    }

    private static string NormalizeLocalReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith('/', StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.Contains('\r') ||
            value.Contains('\n'))
        {
            return "/";
        }

        return value;
    }
}
