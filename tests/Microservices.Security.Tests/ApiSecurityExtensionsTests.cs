using System.Security.Claims;
using Microservices.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microservices.Security.Tests;

public sealed class ApiSecurityExtensionsTests
{
    [Fact]
    public void ProductionConfigurationRequiresHttpsMetadata()
    {
        using var provider = CreateProvider(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["Security:Authority"] = "http://identity.example",
                ["Security:Audience"] = "order-api",
                ["Security:RequireHttpsMetadata"] = "false"
            });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ApiSecurityOptions>>().Value);

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("must be true outside Development", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure =>
            failure.Contains("Authority", StringComparison.Ordinal) &&
            failure.Contains("must use HTTPS", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionConfigurationRejectsInsecureMetadataAddress()
    {
        using var provider = CreateProvider(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["Security:Authority"] = "https://identity.example/realms/order",
                ["Security:MetadataAddress"] =
                    "http://identity.internal/realms/order/.well-known/openid-configuration",
                ["Security:Audience"] = "order-api"
            });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ApiSecurityOptions>>().Value);

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("MetadataAddress", StringComparison.Ordinal) &&
            failure.Contains("must use HTTPS", StringComparison.Ordinal));
    }

    [Fact]
    public void DevelopmentCanExplicitlyUseHttpMetadata()
    {
        using var provider = CreateProvider(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["Security:Authority"] = "http://localhost:8080/realms/order",
                ["Security:Audience"] = "order-api",
                ["Security:RequireHttpsMetadata"] = "false"
            });

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("http://localhost:8080/realms/order", jwt.Authority);
        Assert.False(jwt.RequireHttpsMetadata);
        Assert.True(jwt.IncludeErrorDetails);
    }

    [Fact]
    public void JwtBearerUsesStrictAccessTokenValidationDefaults()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());

        var jwt = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("https://identity.example/realms/order", jwt.Authority);
        Assert.Equal("order-api", jwt.Audience);
        Assert.True(jwt.RequireHttpsMetadata);
        Assert.False(jwt.MapInboundClaims);
        Assert.False(jwt.SaveToken);
        Assert.False(jwt.IncludeErrorDetails);
        Assert.True(jwt.RefreshOnIssuerKeyNotFound);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuer);
        Assert.True(jwt.TokenValidationParameters.ValidateAudience);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(jwt.TokenValidationParameters.ValidateLifetime);
        Assert.True(jwt.TokenValidationParameters.RequireAudience);
        Assert.True(jwt.TokenValidationParameters.RequireExpirationTime);
        Assert.True(jwt.TokenValidationParameters.RequireSignedTokens);
        Assert.Equal("order-api", jwt.TokenValidationParameters.ValidAudience);
        Assert.Equal(
            ["JWT", "at+jwt"],
            jwt.TokenValidationParameters.ValidTypes);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            jwt.TokenValidationParameters.ClockSkew);
        Assert.Equal(
            "preferred_username",
            jwt.TokenValidationParameters.NameClaimType);
        Assert.Equal(
            SecurityClaimTypes.Role,
            jwt.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void ConfigurationRejectsWhitespaceInRoleClientAndNameClaimType()
    {
        var configuration = ValidConfiguration();
        configuration["Security:RoleClientId"] = "order api";
        configuration["Security:NameClaimType"] = "preferred username";

        using var provider = CreateProvider(Environments.Production, configuration);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ApiSecurityOptions>>().Value);

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("RoleClientId", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure =>
            failure.Contains("NameClaimType", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FallbackPolicyRequiresBearerAuthentication()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(policy);
        Assert.Contains(
            JwtBearerDefaults.AuthenticationScheme,
            policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task ScopePolicyParsesSpaceSeparatedAndRepeatedScopeClaims()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = AuthenticatedPrincipal(
            new Claim("scope", "openid orders.read"),
            new Claim("scope", "orders.create orders.cancel"));

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            ScopePolicy.For("orders.cancel"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ScopePolicyUsesExactOrdinalScopeMatching()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = AuthenticatedPrincipal(
            new Claim("scope", "orders.read.all ORDERS.READ"));

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            ScopePolicy.For("orders.read"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RolePolicyUsesApplicationRoleClaim()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = AuthenticatedPrincipal(
            new Claim(SecurityClaimTypes.Role, "order-manager"));

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            RolePolicy.For("order-manager"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ScopePolicyRejectsUnauthenticatedPrincipals()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("scope", "orders.read")]));

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            ScopePolicy.For("orders.read"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ScopePolicyIgnoresClaimsFromUnauthenticatedSecondaryIdentity()
    {
        using var provider = CreateProvider(
            Environments.Production,
            ValidConfiguration());
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(
        [
            new ClaimsIdentity(authenticationType: "test"),
            new ClaimsIdentity([new Claim("scope", "orders.read")])
        ]);

        var result = await authorization.AuthorizeAsync(
            user,
            resource: null,
            ScopePolicy.For("orders.read"));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("orders read")]
    [InlineData("orders\\read")]
    [InlineData("orders\"read")]
    [InlineData("ordérs.read")]
    public void ScopePolicyRejectsInvalidOAuthScopeTokens(string scope)
    {
        Assert.Throws<ArgumentException>(() => ScopePolicy.For(scope));
    }

    [Theory]
    [InlineData("")]
    [InlineData("order admin")]
    [InlineData("\t")]
    public void RolePolicyRejectsInvalidRoles(string role)
    {
        Assert.Throws<ArgumentException>(() => RolePolicy.For(role));
    }

    private static ServiceProvider CreateProvider(
        string environmentName,
        IDictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var environment = new TestHostEnvironment(environmentName);

        services.AddLogging();
        services.AddApiSecurity(configuration, environment);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["Security:Authority"] = "https://identity.example/realms/order",
        ["Security:Audience"] = "order-api",
        ["Security:RoleClientId"] = "order-api"
    };

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(
            claims,
            authenticationType: "test",
            nameType: SecurityClaimTypes.Name,
            roleType: SecurityClaimTypes.Role));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = nameof(Microservices.Security.Tests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
