using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Identity.Api.Tests;

public sealed class AuthorizationServerProtocolTests
{
    [Fact]
    public void ServerEnablesOnlyApprovedFlowsAndRequiresS256Pkce()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Identity.Api.Tests",
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
            DisableDefaults = true
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:identity-db"] =
                "Host=localhost;Database=identity_tests;Username=identity;Password=unused",
            ["AuthorizationServer:Issuer"] = "https://identity.example.test/",
            ["AuthorizationServer:Scopes:0:Name"] = "identity.profile.read",
            ["AuthorizationServer:Scopes:0:DisplayName"] = "Read identity profile",
            ["AuthorizationServer:Scopes:0:Resource"] = "identity-api",
            ["IdentityNotifications:Provider"] = "DevelopmentLog",
            ["IdentityNotifications:PublicOrigin"] = "https://identity.example.test/"
        });
        builder.AddIdentityService();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>()
            .CurrentValue;

        Assert.Contains(OpenIddictConstants.GrantTypes.AuthorizationCode, options.GrantTypes);
        Assert.Contains(OpenIddictConstants.GrantTypes.RefreshToken, options.GrantTypes);
        Assert.Contains(OpenIddictConstants.GrantTypes.ClientCredentials, options.GrantTypes);
        Assert.DoesNotContain(OpenIddictConstants.GrantTypes.Password, options.GrantTypes);
        Assert.DoesNotContain(OpenIddictConstants.GrantTypes.Implicit, options.GrantTypes);
        Assert.True(options.RequireProofKeyForCodeExchange);
        Assert.Contains(OpenIddictConstants.CodeChallengeMethods.Sha256, options.CodeChallengeMethods);
        Assert.DoesNotContain(OpenIddictConstants.CodeChallengeMethods.Plain, options.CodeChallengeMethods);
        Assert.Equal(
            [
                OpenIddictConstants.ClientAuthenticationMethods.ClientSecretBasic,
                OpenIddictConstants.ClientAuthenticationMethods.PrivateKeyJwt
            ],
            options.ClientAuthenticationMethods.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(OpenIddictConstants.ResponseModes.Fragment, options.ResponseModes);
        Assert.DoesNotContain(OpenIddictConstants.PromptValues.Consent, options.PromptValues);
        Assert.DoesNotContain(OpenIddictConstants.PromptValues.SelectAccount, options.PromptValues);
        Assert.Contains("identity.profile.read", options.Scopes);
        Assert.True(options.DisableAccessTokenEncryption);
        Assert.True(options.UseReferenceRefreshTokens);
        Assert.Equal(TimeSpan.Zero, options.RefreshTokenReuseLeeway);
        Assert.Contains(
            options.PushedAuthorizationEndpointUris,
            uri => uri.OriginalString == "/connect/par");
        Assert.Equal(
            220_000,
            provider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value.IterationCount);
    }
}
