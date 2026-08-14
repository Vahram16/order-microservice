using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Product.Api.Persistence;

namespace Product.Api.Tests;

public sealed class ProductApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://product-tests.example/realms/order";
    private const string Audience = "product-api";
    private const string AuthorizedParty = "product-scalar-dev";
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__product-db";
    private static readonly SymmetricSecurityKey SigningKey = new(
        RandomNumberGenerator.GetBytes(64));
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("PRODUCT_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "PRODUCT_TEST_CONNECTION_STRING is required for Product API integration tests.");

    public ProductApiFactory()
    {
        // Minimal-hosting Program reads connection strings while composing services,
        // before WebApplicationFactory's configuration callback executes.
        Environment.SetEnvironmentVariable(
            ConnectionStringEnvironmentVariable,
            _connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Authority"] = Issuer,
                ["Security:Audience"] = Audience,
                ["Security:RoleClientId"] = Audience,
                ["Security:ValidAuthorizedParties:0"] = AuthorizedParty,
                ["Security:RequireHttpsMetadata"] = "true"
            }));
        builder.ConfigureServices(services =>
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = Issuer
                    };
                    configuration.SigningKeys.Add(SigningKey);

                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.RequireHttpsMetadata = false;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.ValidAudience = Audience;
                    options.TokenValidationParameters.IssuerSigningKey = SigningKey;
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                    options.TokenValidationParameters.ValidateLifetime = true;
                    options.TokenValidationParameters.RoleClaimType = "role";
                }));
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        await dbContext.Database.MigrateAsync();
        await dbContext.Products.ExecuteDeleteAsync();
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken());
        return client;
    }

    private static string CreateAccessToken()
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "product-integration-user",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["azp"] = AuthorizedParty
        };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = now.AddMinutes(10).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                SigningKey,
                SecurityAlgorithms.HmacSha256)
        });
    }
}
