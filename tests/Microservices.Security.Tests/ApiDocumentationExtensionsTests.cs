using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microservices.Security.Tests;

public sealed class ApiDocumentationExtensionsTests
{
    [Fact]
    public async Task OpenApiOAuthFlowUsesTheConfiguredKeycloakRealm()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:Authority"] = "https://identity.example/realms/order"
        });
        builder.AddApiDocumentation("Test API");

        await using var app = builder.Build();
        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync();
        var flow = document.Components!.SecuritySchemes!["OAuth2"].Flows!.AuthorizationCode!;

        Assert.Equal(
            new Uri("https://identity.example/realms/order/protocol/openid-connect/auth"),
            flow.AuthorizationUrl);
        Assert.Equal(
            new Uri("https://identity.example/realms/order/protocol/openid-connect/token"),
            flow.TokenUrl);
        Assert.Equal(
            [
                "email",
                "openid",
                "orders.cancel",
                "orders.create",
                "orders.read",
                "profile"
            ],
            flow.Scopes!.Keys.Order(StringComparer.Ordinal));
    }
}
