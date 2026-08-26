using System.Text.Json;

namespace Payment.Api.Tests;

public sealed class PaymentSecurityConfigurationTests
{
    [Fact]
    public void DevelopmentRealmDefinesSharedBackendRolesForPaymentCapabilities()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AppHost",
            "Microservices.AppHost",
            "Keycloak",
            "order-realm.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var backendApi = root.GetProperty("clients")
            .EnumerateArray()
            .Single(client => client.GetProperty("clientId").GetString() == "backend-api");
        Assert.True(backendApi.GetProperty("bearerOnly").GetBoolean());
        Assert.False(backendApi.GetProperty("fullScopeAllowed").GetBoolean());

        var clientScopes = root.GetProperty("clientScopes")
            .EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("backend-api-audience", clientScopes);
        Assert.Contains("backend-api-roles", clientScopes);
        Assert.DoesNotContain("payments.read", clientScopes);
        Assert.DoesNotContain("payments.manage", clientScopes);

        var backendMappings = root.GetProperty("clientScopeMappings")
            .GetProperty("backend-api")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(backendMappings, mapping =>
            mapping.GetProperty("client").GetString() == "mobile-app" &&
            mapping.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ToHashSet()
                .IsSupersetOf(["payments.read", "payments.manage"]));
    }

    [Fact]
    public void DevelopmentRealmBundlesBackendPermissionsIntoCustomerAndAdminRoles()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AppHost",
            "Microservices.AppHost",
            "Keycloak",
            "order-realm.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var realmRoles = document.RootElement.GetProperty("roles").GetProperty("realm")
            .EnumerateArray()
            .ToDictionary(role => role.GetProperty("name").GetString()!, StringComparer.Ordinal);

        Assert.Equal(
            [
                "customers.addresses.write",
                "customers.self.read",
                "customers.self.update",
                "payments.manage",
                "payments.read",
                "product.read"
            ],
            BackendApiComposites(realmRoles["customer"]));
        Assert.Equal(
            [
                "customers.addresses.write",
                "customers.self.delete",
                "customers.self.export",
                "customers.self.read",
                "customers.self.update",
                "payments.manage",
                "payments.read",
                "product.manage",
                "product.read"
            ],
            BackendApiComposites(realmRoles["admin"]));
    }

    private static string[] BackendApiComposites(JsonElement realmRole) =>
        realmRole.GetProperty("composites").GetProperty("client").GetProperty("backend-api")
            .EnumerateArray()
            .Select(role => role.GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
