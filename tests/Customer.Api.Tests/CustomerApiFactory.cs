using System.Net.Http.Headers;
using System.Security.Cryptography;
using Customer.Api.Persistence;
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

namespace Customer.Api.Tests;

public sealed class CustomerApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://customer-tests.example/realms/order";
    private const string Audience = "customer-api";
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__customer-db";
    private const string RabbitMqConnectionStringEnvironmentVariable =
        "ConnectionStrings__rabbitmq";
    private const string RabbitMqUseTlsEnvironmentVariable =
        "Messaging__UseTls";
    private static readonly SymmetricSecurityKey SigningKey = new(
        RandomNumberGenerator.GetBytes(64));
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("CUSTOMER_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "CUSTOMER_TEST_CONNECTION_STRING is required for Customer API integration tests.");
    private readonly string _rabbitMqConnectionString =
        Environment.GetEnvironmentVariable("MESSAGING_TEST_RABBITMQ_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "MESSAGING_TEST_RABBITMQ_CONNECTION_STRING is required for Customer API integration tests.");

    public CustomerApiFactory()
    {
        // Minimal-hosting Program reads connection strings and messaging transport settings while
        // it composes services, before WebApplicationFactory's ConfigureAppConfiguration callback.
        Environment.SetEnvironmentVariable(
            ConnectionStringEnvironmentVariable,
            _connectionString);
        Environment.SetEnvironmentVariable(
            RabbitMqConnectionStringEnvironmentVariable,
            _rabbitMqConnectionString);
        Environment.SetEnvironmentVariable(
            RabbitMqUseTlsEnvironmentVariable,
            bool.FalseString);
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
                ["Security:ValidAuthorizedParties:0"] = "order-mobile",
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
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        await dbContext.Database.MigrateAsync();
        await ResetAsync();
    }

    public HttpClient CreateAuthenticatedClient(
        string subject,
        params string[] scopes)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken(subject, scopes));
        return client;
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        await dbContext.CustomerAuditEntries.ExecuteDeleteAsync();
        await dbContext.CustomerAddresses.ExecuteDeleteAsync();
        await dbContext.Customers.ExecuteDeleteAsync();
    }

    public async Task<CustomerDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        var dbContext = new CustomerDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        return dbContext;
    }

    private static string CreateAccessToken(string subject, IReadOnlyCollection<string> scopes)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["azp"] = "order-mobile",
            ["scope"] = string.Join(' ', scopes),
            ["given_name"] = "Ada",
            ["family_name"] = "Lovelace",
            ["email"] = "ada@example.com",
            ["email_verified"] = true,
            ["resource_access"] = new Dictionary<string, object>
            {
                [Audience] = new Dictionary<string, object>
                {
                    ["roles"] = new[] { "customer-user" }
                }
            }
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
