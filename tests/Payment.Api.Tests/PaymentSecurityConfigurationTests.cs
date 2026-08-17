using System.Text.Json;

namespace Payment.Api.Tests;

public sealed class PaymentSecurityConfigurationTests
{
    [Fact]
    public void DevelopmentRealmDefinesPaymentResourceServerAndCapabilities()
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

        var paymentApi = root.GetProperty("clients")
            .EnumerateArray()
            .Single(client => client.GetProperty("clientId").GetString() == "payment-api");
        Assert.True(paymentApi.GetProperty("bearerOnly").GetBoolean());
        Assert.False(paymentApi.GetProperty("fullScopeAllowed").GetBoolean());

        var paymentScopes = root.GetProperty("clientScopes")
            .EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("payment-api-audience", paymentScopes);
        Assert.Contains("payment-api-roles", paymentScopes);
        Assert.Contains("payments.methods.read", paymentScopes);
        Assert.Contains("payments.methods.write", paymentScopes);

        var paymentMappings = root.GetProperty("clientScopeMappings")
            .GetProperty("payment-api")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(paymentMappings, mapping =>
            mapping.GetProperty("client").GetString() == "order-mobile" &&
            mapping.GetProperty("roles").EnumerateArray().Any(role => role.GetString() == "payment-user"));
        Assert.Contains(paymentMappings, mapping =>
            mapping.GetProperty("client").GetString() == "payment-scalar-dev" &&
            mapping.GetProperty("roles").EnumerateArray().Any(role => role.GetString() == "payment-user"));
    }

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
