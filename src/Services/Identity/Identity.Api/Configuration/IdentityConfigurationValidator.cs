using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Identity.Api.Configuration;

internal static partial class IdentityConfigurationValidator
{
    public static void Validate(
        AuthorizationServerOptions options,
        IdentityNotificationOptions notifications,
        IHostEnvironment environment)
    {
        var failures = new List<string>();

        ValidateIssuer(options.Issuer, environment, failures);
        if (!environment.IsDevelopment() && options.UseEphemeralKeysInDevelopment)
        {
            failures.Add(
                "'AuthorizationServer:UseEphemeralKeysInDevelopment' is forbidden outside Development.");
        }

        ValidateLifetimes(options, failures);
        ValidateCertificates(options, environment, failures);
        ValidateScopes(options, failures);
        ValidateCorsOrigins(options.CorsOrigins, environment, failures);
        ValidateNotifications(notifications, environment, failures);

        ThrowIfInvalid(failures);
    }

    public static void ValidateProvisioning(
        AuthorizationServerOptions options,
        IHostEnvironment environment)
    {
        var failures = new List<string>();
        ValidateScopesAndClients(options, environment, failures);
        ThrowIfInvalid(failures);
    }

    private static void ValidateIssuer(
        string? value,
        IHostEnvironment environment,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add("'AuthorizationServer:Issuer' is required outside Development.");
            }

            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var issuer) ||
            string.IsNullOrWhiteSpace(issuer.Host) ||
            (issuer.Scheme != Uri.UriSchemeHttps && issuer.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(issuer.UserInfo) ||
            !string.IsNullOrEmpty(issuer.Query) ||
            !string.IsNullOrEmpty(issuer.Fragment))
        {
            failures.Add(
                "'AuthorizationServer:Issuer' must be an absolute HTTP(S) URI without user information, a query, or a fragment.");
        }
        else if (!environment.IsDevelopment() && issuer.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("'AuthorizationServer:Issuer' must use HTTPS outside Development.");
        }
    }

    private static void ValidateLifetimes(
        AuthorizationServerOptions options,
        List<string> failures)
    {
        ValidateLifetime(options.AuthorizationCodeLifetime, nameof(options.AuthorizationCodeLifetime),
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), failures);
        ValidateLifetime(options.AccessTokenLifetime, nameof(options.AccessTokenLifetime),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30), failures);
        ValidateLifetime(options.IdentityTokenLifetime, nameof(options.IdentityTokenLifetime),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30), failures);
        ValidateLifetime(options.RefreshTokenLifetime, nameof(options.RefreshTokenLifetime),
            TimeSpan.FromHours(1), TimeSpan.FromDays(30), failures);
    }

    private static void ValidateLifetime(
        TimeSpan value,
        string name,
        TimeSpan minimum,
        TimeSpan maximum,
        List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"'AuthorizationServer:{name}' must be between {minimum} and {maximum}.");
        }
    }

    private static void ValidateCertificates(
        AuthorizationServerOptions options,
        IHostEnvironment environment,
        List<string> failures)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (options.SigningCertificates.Count == 0)
        {
            failures.Add("At least one production signing certificate is required.");
        }

        if (options.EncryptionCertificates.Count == 0)
        {
            failures.Add("At least one production encryption certificate is required.");
        }

        var signingPaths = options.SigningCertificates
            .Select(certificate => certificate.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.Ordinal);
        var encryptionPaths = options.EncryptionCertificates
            .Select(certificate => certificate.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.Ordinal);

        if (signingPaths.Overlaps(encryptionPaths))
        {
            failures.Add("Signing and encryption certificates must be distinct.");
        }

        foreach (var certificate in options.SigningCertificates.Concat(options.EncryptionCertificates))
        {
            if (string.IsNullOrWhiteSpace(certificate.Path))
            {
                failures.Add("Every production certificate entry requires a path.");
            }
        }
    }

    private static void ValidateScopesAndClients(
        AuthorizationServerOptions options,
        IHostEnvironment environment,
        List<string> failures)
    {
        var scopes = ValidateScopes(options, failures);
        ValidateClients(options.Clients, scopes, environment, failures);
    }

    private static HashSet<string> ValidateScopes(
        AuthorizationServerOptions options,
        List<string> failures)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in options.Scopes)
        {
            if (!ScopeNameExpression().IsMatch(scope.Name))
            {
                failures.Add($"Scope '{scope.Name}' must use lowercase dotted capability notation.");
            }
            else if (scope.Name.Length > 200)
            {
                failures.Add($"Scope '{scope.Name}' exceeds the 200-character storage limit.");
            }
            else if (!scopes.Add(scope.Name))
            {
                failures.Add($"Scope '{scope.Name}' is configured more than once.");
            }

            if (string.IsNullOrWhiteSpace(scope.DisplayName) ||
                string.IsNullOrWhiteSpace(scope.Resource))
            {
                failures.Add($"Scope '{scope.Name}' requires a display name and resource.");
            }
            else if (scope.Resource.Length > 200 || scope.Resource.Any(char.IsWhiteSpace))
            {
                failures.Add($"Scope '{scope.Name}' has invalid resource '{scope.Resource}'.");
            }
        }

        return scopes;
    }

    private static void ValidateClients(
        IEnumerable<AuthorizationClientOptions> configuredClients,
        HashSet<string> scopes,
        IHostEnvironment environment,
        List<string> failures)
    {

        var standardScopes = new HashSet<string>(StringComparer.Ordinal)
        {
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles,
            OpenIddictConstants.Scopes.OfflineAccess
        };
        var clients = new HashSet<string>(StringComparer.Ordinal);

        foreach (var client in configuredClients)
        {
            if (string.IsNullOrWhiteSpace(client.ClientId) ||
                client.ClientId.Length > 100 ||
                client.ClientId.Any(char.IsWhiteSpace) ||
                !clients.Add(client.ClientId))
            {
                failures.Add(
                    $"Client id '{client.ClientId}' is blank, duplicated, or exceeds 100 characters.");
            }

            if (string.IsNullOrWhiteSpace(client.DisplayName) ||
                client.DisplayName.Length > 200)
            {
                failures.Add(
                    $"Client '{client.ClientId}' requires a display name of at most 200 characters.");
            }

            if (client.Scopes.Count != client.Scopes.Distinct(StringComparer.Ordinal).Count())
            {
                failures.Add($"Client '{client.ClientId}' configures a scope more than once.");
            }

            foreach (var scope in client.Scopes)
            {
                if (!scopes.Contains(scope) && !standardScopes.Contains(scope))
                {
                    failures.Add($"Client '{client.ClientId}' references unknown scope '{scope}'.");
                }
            }

            ValidateClientCredentials(client, environment, failures);
            ValidateClientCapabilities(client, standardScopes, failures);
            ValidateClientUris(client, environment, failures);
        }
    }

    private static void ValidateClientCapabilities(
        AuthorizationClientOptions client,
        HashSet<string> standardScopes,
        List<string> failures)
    {
        if (client.Profile == AuthorizationClientProfile.Service)
        {
            if (client.AllowRefreshTokens || client.RequirePushedAuthorizationRequests)
            {
                failures.Add(
                    $"Service client '{client.ClientId}' cannot enable refresh tokens or pushed authorization requests.");
            }

            if (client.Scopes.Any(standardScopes.Contains))
            {
                failures.Add(
                    $"Service client '{client.ClientId}' cannot request user identity or offline-access scopes.");
            }
        }
        else if (client.Scopes.Contains(OpenIddictConstants.Scopes.OfflineAccess) &&
                 !client.AllowRefreshTokens)
        {
            failures.Add(
                $"Interactive client '{client.ClientId}' requires AllowRefreshTokens for offline_access.");
        }
    }

    private static void ValidateClientCredentials(
        AuthorizationClientOptions client,
        IHostEnvironment environment,
        List<string> failures)
    {
        var hasSecret = !string.IsNullOrWhiteSpace(client.ClientSecret);
        var hasKeySet = !string.IsNullOrWhiteSpace(client.JsonWebKeySetPath);

        if (client.Profile == AuthorizationClientProfile.Public)
        {
            if (hasSecret || hasKeySet)
            {
                failures.Add($"Public client '{client.ClientId}' must not have client credentials.");
            }

            return;
        }

        if (hasSecret == hasKeySet)
        {
            failures.Add(
                $"Confidential client '{client.ClientId}' must configure exactly one client secret or JSON Web Key Set.");
        }
        else if (hasSecret && !environment.IsDevelopment() &&
                 !IsSecretManagerGeneratedValue(client.ClientSecret!, 43, 256))
        {
            failures.Add(
                $"Client secret for '{client.ClientId}' must be a secret-manager-generated Base64url value between 43 and 256 characters.");
        }
    }

    private static void ValidateCorsOrigins(
        IEnumerable<string> configuredOrigins,
        IHostEnvironment environment,
        List<string> failures)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in configuredOrigins)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
                string.IsNullOrWhiteSpace(origin.Host) ||
                (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp) ||
                !string.IsNullOrEmpty(origin.UserInfo) ||
                !string.IsNullOrEmpty(origin.PathAndQuery.Trim('/')) ||
                !string.IsNullOrEmpty(origin.Fragment) ||
                value.Length > 2048)
            {
                failures.Add($"CORS origin '{value}' must be an absolute HTTP(S) origin URI.");
            }
            else if (!environment.IsDevelopment() && origin.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add($"CORS origin '{value}' must use HTTPS outside Development.");
            }
            else if (!origins.Add(origin.GetLeftPart(UriPartial.Authority)))
            {
                failures.Add($"CORS origin '{value}' is configured more than once.");
            }
        }
    }

    private static void ValidateClientUris(
        AuthorizationClientOptions client,
        IHostEnvironment environment,
        List<string> failures)
    {
        var isInteractive = client.Profile is
            AuthorizationClientProfile.Public or AuthorizationClientProfile.Web;

        if (isInteractive && client.RedirectUris.Count == 0)
        {
            failures.Add($"Interactive client '{client.ClientId}' requires at least one redirect URI.");
        }

        if (!isInteractive &&
            (client.RedirectUris.Count != 0 || client.PostLogoutRedirectUris.Count != 0))
        {
            failures.Add($"Service client '{client.ClientId}' cannot configure browser redirect URIs.");
        }

        foreach (var value in client.RedirectUris.Concat(client.PostLogoutRedirectUris))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                failures.Add($"Client '{client.ClientId}' has invalid redirect URI '{value}'.");
            }
            else if (!environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add($"Client '{client.ClientId}' redirect URIs must use HTTPS outside Development.");
            }
        }
    }

    private static void ValidateNotifications(
        IdentityNotificationOptions options,
        IHostEnvironment environment,
        List<string> failures)
    {
        if (options.DispatchInterval < TimeSpan.FromSeconds(1) ||
            options.DispatchInterval > TimeSpan.FromMinutes(5))
        {
            failures.Add(
                "'IdentityNotifications:DispatchInterval' must be between one second and five minutes.");
        }

        if (options.LeaseDuration <= options.DispatchInterval ||
            options.LeaseDuration > TimeSpan.FromMinutes(30))
        {
            failures.Add(
                "'IdentityNotifications:LeaseDuration' must exceed DispatchInterval and be at most 30 minutes.");
        }

        if (options.DeduplicationWindow < TimeSpan.FromMinutes(1) ||
            options.DeduplicationWindow > TimeSpan.FromHours(24))
        {
            failures.Add(
                "'IdentityNotifications:DeduplicationWindow' must be between one minute and 24 hours.");
        }

        if (options.BatchSize is < 1 or > 100 || options.MaximumAttempts is < 1 or > 50)
        {
            failures.Add(
                "Identity notification BatchSize must be 1-100 and MaximumAttempts must be 1-50.");
        }

        if (!Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin) ||
            string.IsNullOrWhiteSpace(origin.Host) ||
            (origin.Scheme != Uri.UriSchemeHttps && origin.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.PathAndQuery.Trim('/')) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            failures.Add("'IdentityNotifications:PublicOrigin' must be an absolute origin URI.");
        }
        else if (!environment.IsDevelopment() && origin.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("'IdentityNotifications:PublicOrigin' must use HTTPS outside Development.");
        }

        if (!environment.IsDevelopment() &&
            options.Provider is IdentityNotificationProvider.None or
                IdentityNotificationProvider.DevelopmentLog)
        {
            failures.Add("A production identity notification provider is required.");
        }

        if (options.Provider == IdentityNotificationProvider.Webhook &&
            (!Uri.TryCreate(options.WebhookEndpoint, UriKind.Absolute, out var webhook) ||
             string.IsNullOrWhiteSpace(webhook.Host) ||
             (webhook.Scheme != Uri.UriSchemeHttps && webhook.Scheme != Uri.UriSchemeHttp) ||
             !string.IsNullOrEmpty(webhook.UserInfo) ||
             !string.IsNullOrEmpty(webhook.Fragment) ||
             (!environment.IsDevelopment() && webhook.Scheme != Uri.UriSchemeHttps)))
        {
            failures.Add("The identity notification webhook must be an absolute HTTPS URI in production.");
        }

        if (!environment.IsDevelopment() &&
            options.Provider == IdentityNotificationProvider.Webhook &&
            !IsSecretManagerGeneratedValue(options.WebhookApiKey, 43, 512))
        {
            failures.Add(
                "The production identity notification webhook requires a secret-manager-generated Base64url API key between 43 and 512 characters.");
        }
    }

    private static bool IsSecretManagerGeneratedValue(
        string? value,
        int minimumLength,
        int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= minimumLength &&
        value.Length <= maximumLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_') &&
        value.Distinct().Count() >= 16;

    private static void ThrowIfInvalid(List<string> failures)
    {
        if (failures.Count != 0)
        {
            throw new OptionsValidationException(
                AuthorizationServerOptions.SectionName,
                typeof(AuthorizationServerOptions),
                failures);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeNameExpression();
}
