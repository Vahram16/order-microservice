using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;
using Payment.Api.Webhooks;

namespace Payment.Api.Tests;

public sealed class PaymentApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://payment-tests.example/realms/order";
    private const string Audience = "payment-api";
    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__payment-db";
    private const string RabbitMqConnectionStringEnvironmentVariable = "ConnectionStrings__rabbitmq";
    private const string RabbitMqUseTlsEnvironmentVariable = "Messaging__UseTls";
    private static readonly SymmetricSecurityKey SigningKey = new(
        RandomNumberGenerator.GetBytes(64));

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "PAYMENT_TEST_CONNECTION_STRING is required for Payment API integration tests.");

    private readonly string _rabbitMqConnectionString =
        Environment.GetEnvironmentVariable("MESSAGING_TEST_RABBITMQ_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "MESSAGING_TEST_RABBITMQ_CONNECTION_STRING is required for Payment API integration tests.");

    public PaymentApiFactory()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvironmentVariable, _connectionString);
        Environment.SetEnvironmentVariable(
            RabbitMqConnectionStringEnvironmentVariable,
            _rabbitMqConnectionString);
        Environment.SetEnvironmentVariable(RabbitMqUseTlsEnvironmentVariable, bool.FalseString);
        Environment.SetEnvironmentVariable("Stripe__SecretKey", "sk_test_payment_integration");
        Environment.SetEnvironmentVariable("Stripe__WebhookSecret", "whsec_payment_integration");
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
        {
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(new TestPaymentProvider());
            services.RemoveAll<IPaymentWebhookVerifier>();
            services.AddSingleton<IPaymentWebhookVerifier>(new TestPaymentWebhookVerifier());

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
                });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The application starts MassTransit hosted services immediately. Apply the real Payment
        // migrations first so the EF outbox dispatcher never races schema creation in integration tests.
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        using (var dbContext = new PaymentDbContext(options))
        {
            dbContext.Database.Migrate();
        }

        return base.CreateHost(builder);
    }

    public Task InitializeDatabaseAsync() => ResetAsync();

    public HttpClient CreateAuthenticatedClient(string subject, params string[] scopes)
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
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await dbContext.PaymentMethods.ExecuteDeleteAsync();
        await dbContext.PaymentWebhookEvents.ExecuteDeleteAsync();
        await dbContext.PaymentMethodSetupOperations.ExecuteDeleteAsync();
        await dbContext.PaymentCustomers.ExecuteDeleteAsync();
    }

    public async Task<PaymentDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        var dbContext = new PaymentDbContext(options);
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
            ["resource_access"] = new Dictionary<string, object>
            {
                [Audience] = new Dictionary<string, object>
                {
                    ["roles"] = new[] { "payment-user" }
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

    private sealed class TestPaymentProvider : IPaymentProvider
    {
        public const string CustomerId = "cus_payment_integration";
        public const string SetupIntentId = "seti_payment_integration";
        public const string PaymentMethodId = "pm_payment_integration";

        public Task<string> CreateCustomerAsync(
            Guid paymentCustomerId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(CustomerId);

        public Task<PaymentMethodSetupSession> CreatePaymentMethodSetupAsync(
            Guid paymentCustomerId,
            string providerCustomerId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PaymentMethodSetupSession(
                SetupIntentId,
                $"{SetupIntentId}_secret_test",
                "requires_confirmation",
                CustomerId));

        public Task<PaymentMethodSetupSession> GetPaymentMethodSetupAsync(
            string providerSetupIntentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PaymentMethodSetupSession(
                SetupIntentId,
                string.Empty,
                "succeeded",
                CustomerId,
                PaymentMethodId));

        public Task<ProviderPaymentMethod> GetPaymentMethodAsync(
            string providerPaymentMethodId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderPaymentMethod(
                PaymentMethodId,
                CustomerId,
                "visa",
                "4242",
                12,
                2030,
                null));
    }

    private sealed class TestPaymentWebhookVerifier : IPaymentWebhookVerifier
    {
        public PaymentWebhookNotification? Verify(string payload, string signature)
        {
            if (!string.Equals(signature, "test-signature", StringComparison.Ordinal))
            {
                throw new PaymentWebhookVerificationException(
                    new InvalidOperationException("Invalid test signature."));
            }

            return new PaymentWebhookNotification(
                payload,
                "setup_intent.succeeded",
                TestPaymentProvider.SetupIntentId);
        }
    }
}
