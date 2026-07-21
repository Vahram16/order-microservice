using System.Text.Json;
using Identity.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Provisioning;

public sealed partial class AuthorizationServerProvisioner(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IOptions<AuthorizationServerOptions> options,
    IHostEnvironment environment,
    ILogger<AuthorizationServerProvisioner> logger)
{
    internal const string OwnershipProperty = "microservices.identity.provisioning.owner";
    internal const string OwnershipValue = "identity-migrator/v1";

    public async Task ProvisionAsync(CancellationToken cancellationToken = default)
    {
        var configuredScopes = options.Value.Scopes;
        var configuredClients = options.Value.Clients;

        foreach (var scope in configuredScopes)
        {
            await ProvisionScopeAsync(scope, cancellationToken);
        }

        foreach (var client in configuredClients)
        {
            await ProvisionClientAsync(client, cancellationToken);
        }

        if (configuredScopes.Count == 0 || configuredClients.Count == 0)
        {
            LogPruningSkipped(logger, configuredScopes.Count, configuredClients.Count);
            return;
        }

        await RemoveStaleClientsAsync(
            configuredClients.Select(client => client.ClientId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
        await RemoveStaleScopesAsync(
            configuredScopes.Select(scope => scope.Name).ToHashSet(StringComparer.Ordinal),
            cancellationToken);
    }

    private async Task ProvisionScopeAsync(
        AuthorizationScopeOptions configuration,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = configuration.Name,
            DisplayName = configuration.DisplayName,
            Resources = { configuration.Resource }
        };
        var scope = await scopeManager.FindByNameAsync(
            configuration.Name,
            cancellationToken);

        if (scope is null)
        {
            MarkAsManaged(descriptor.Properties);
            await scopeManager.CreateAsync(descriptor, cancellationToken);
            LogProvisionedScope(logger, configuration.Name);
            return;
        }

        var properties = await scopeManager.GetPropertiesAsync(
            scope,
            cancellationToken);
        EnsureManaged(properties, "scope", configuration.Name);
        CopyProperties(properties, descriptor.Properties);
        MarkAsManaged(descriptor.Properties);
        await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
        LogReconciledScope(logger, configuration.Name);
    }

    private async Task ProvisionClientAsync(
        AuthorizationClientOptions configuration,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = configuration.ClientId,
            DisplayName = configuration.DisplayName,
            ClientType = configuration.Profile == AuthorizationClientProfile.Public
                ? ClientTypes.Public
                : ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit
        };

        if (!string.IsNullOrWhiteSpace(configuration.ClientSecret))
        {
            descriptor.ClientSecret = configuration.ClientSecret;
        }

        if (!string.IsNullOrWhiteSpace(configuration.JsonWebKeySetPath))
        {
            descriptor.JsonWebKeySet = LoadPublicKeySet(configuration.JsonWebKeySetPath);
        }

        foreach (var value in configuration.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(value, UriKind.Absolute));
        }

        foreach (var value in configuration.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(value, UriKind.Absolute));
        }

        AddPermissions(descriptor, configuration);

        var application = await applicationManager.FindByClientIdAsync(
            configuration.ClientId,
            cancellationToken);
        if (application is null)
        {
            MarkAsManaged(descriptor.Properties);
            await applicationManager.CreateAsync(descriptor, cancellationToken);
            LogProvisionedClient(logger, configuration.ClientId);
            return;
        }

        var properties = await applicationManager.GetPropertiesAsync(
            application,
            cancellationToken);
        EnsureManaged(properties, "client", configuration.ClientId);
        CopyProperties(properties, descriptor.Properties);
        MarkAsManaged(descriptor.Properties);
        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        LogReconciledClient(logger, configuration.ClientId);
    }

    private async Task RemoveStaleClientsAsync(
        IReadOnlySet<string> configuredClientIds,
        CancellationToken cancellationToken)
    {
        var applications = new List<object>();
        await foreach (var application in applicationManager.ListAsync(
                           count: null,
                           offset: null,
                           cancellationToken))
        {
            applications.Add(application);
        }

        foreach (var application in applications)
        {
            var properties = await applicationManager.GetPropertiesAsync(
                application,
                cancellationToken);
            var clientId = await applicationManager.GetClientIdAsync(
                application,
                cancellationToken);
            if (!ShouldRemove(properties, clientId, configuredClientIds))
            {
                continue;
            }

            await applicationManager.DeleteAsync(application, cancellationToken);
            LogRemovedClient(logger, clientId ?? "<unknown>");
        }
    }

    private async Task RemoveStaleScopesAsync(
        IReadOnlySet<string> configuredScopeNames,
        CancellationToken cancellationToken)
    {
        var scopes = new List<object>();
        await foreach (var scope in scopeManager.ListAsync(
                           count: null,
                           offset: null,
                           cancellationToken))
        {
            scopes.Add(scope);
        }

        foreach (var scope in scopes)
        {
            var properties = await scopeManager.GetPropertiesAsync(scope, cancellationToken);
            var name = await scopeManager.GetNameAsync(scope, cancellationToken);
            if (!ShouldRemove(properties, name, configuredScopeNames))
            {
                continue;
            }

            await scopeManager.DeleteAsync(scope, cancellationToken);
            LogRemovedScope(logger, name ?? "<unknown>");
        }
    }

    internal static bool ShouldRemove(
        IReadOnlyDictionary<string, JsonElement> properties,
        string? identifier,
        IReadOnlySet<string> configuredIdentifiers) =>
        IsManaged(properties) &&
        (identifier is null || !configuredIdentifiers.Contains(identifier));

    internal static bool IsManaged(IReadOnlyDictionary<string, JsonElement> properties) =>
        properties.TryGetValue(OwnershipProperty, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), OwnershipValue, StringComparison.Ordinal);

    internal static void EnsureManaged(
        IReadOnlyDictionary<string, JsonElement> properties,
        string resourceType,
        string identifier)
    {
        if (!IsManaged(properties))
        {
            throw new InvalidOperationException(
                $"The OAuth {resourceType} '{identifier}' already exists but is not owned by this provisioner. " +
                "Refusing to adopt or overwrite an operator-managed registration.");
        }
    }

    private static void CopyProperties(
        IReadOnlyDictionary<string, JsonElement> source,
        Dictionary<string, JsonElement> destination)
    {
        foreach (var property in source)
        {
            destination[property.Key] = property.Value;
        }
    }

    private static void MarkAsManaged(Dictionary<string, JsonElement> properties) =>
        properties[OwnershipProperty] = JsonSerializer.SerializeToElement(OwnershipValue);

    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Information,
        Message = "Provisioned OAuth scope {ScopeName}")]
    private static partial void LogProvisionedScope(ILogger logger, string scopeName);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Information,
        Message = "Reconciled OAuth scope {ScopeName}")]
    private static partial void LogReconciledScope(ILogger logger, string scopeName);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Information,
        Message = "Provisioned OAuth client {ClientId}")]
    private static partial void LogProvisionedClient(ILogger logger, string clientId);

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Information,
        Message = "Reconciled OAuth client {ClientId}")]
    private static partial void LogReconciledClient(ILogger logger, string clientId);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Information,
        Message = "Removed stale managed OAuth client {ClientId}")]
    private static partial void LogRemovedClient(ILogger logger, string clientId);

    [LoggerMessage(
        EventId = 1305,
        Level = LogLevel.Information,
        Message = "Removed stale managed OAuth scope {ScopeName}")]
    private static partial void LogRemovedScope(ILogger logger, string scopeName);

    [LoggerMessage(
        EventId = 1306,
        Level = LogLevel.Warning,
        Message = "Skipped managed OAuth pruning because the provisioning manifest is incomplete (scopes: {ScopeCount}, clients: {ClientCount})")]
    private static partial void LogPruningSkipped(
        ILogger logger,
        int scopeCount,
        int clientCount);

    private static void AddPermissions(
        OpenIddictApplicationDescriptor descriptor,
        AuthorizationClientOptions configuration)
    {
        if (configuration.Profile == AuthorizationClientProfile.Service)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        }
        else
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

            if (configuration.AllowRefreshTokens)
            {
                descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            }

            if (configuration.RequirePushedAuthorizationRequests)
            {
                descriptor.Permissions.Add(Permissions.Endpoints.PushedAuthorization);
                descriptor.Requirements.Add(
                    Requirements.Features.PushedAuthorizationRequests);
            }
        }

        foreach (var scope in configuration.Scopes)
        {
            switch (scope)
            {
                case Scopes.Email:
                    descriptor.Permissions.Add(Permissions.Scopes.Email);
                    break;
                case Scopes.Profile:
                    descriptor.Permissions.Add(Permissions.Scopes.Profile);
                    break;
                case Scopes.Roles:
                    descriptor.Permissions.Add(Permissions.Scopes.Roles);
                    break;
                case Scopes.OpenId or Scopes.OfflineAccess:
                    break;
                default:
                    descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
                    break;
            }
        }
    }

    private JsonWebKeySet LoadPublicKeySet(string configuredPath)
    {
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, environment.ContentRootPath);
        var keySet = new JsonWebKeySet(File.ReadAllText(path));
        if (keySet.Keys.Count == 0 || keySet.Keys.Any(key => key.HasPrivateKey))
        {
            throw new InvalidOperationException(
                $"Client key set '{path}' must contain public keys only.");
        }

        return keySet;
    }
}
