using Identity.Api.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Api.Tests;

public sealed class IdentityConfigurationTests
{
    private const string GeneratedSecret =
        "n4bQgYhMfWWaL-qgxVrQFaO_TxsrC4Is0V1sFbDwCgg";

    [Fact]
    public void ProductionRuntimeRejectsInsecureIssuerCredentialsAndNotifications()
    {
        var options = CreateAuthorizationOptions(
            issuer: "http://identity.example.com/",
            signingPath: "/keys/shared.pfx",
            encryptionPath: "/keys/shared.pfx");
        var notifications = new IdentityNotificationOptions
        {
            Provider = IdentityNotificationProvider.DevelopmentLog,
            PublicOrigin = "http://identity.example.com/"
        };

        var exception = Assert.Throws<OptionsValidationException>(() =>
            IdentityConfigurationValidator.Validate(
                options,
                notifications,
                CreateEnvironment(Environments.Production)));

        Assert.Contains("Issuer' must use HTTPS", exception.Message, StringComparison.Ordinal);
        Assert.Contains("certificates must be distinct", exception.Message, StringComparison.Ordinal);
        Assert.Contains("production identity notification provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRuntimeAcceptsHardenedConfiguration()
    {
        var notifications = new IdentityNotificationOptions
        {
            Provider = IdentityNotificationProvider.Webhook,
            PublicOrigin = "https://identity.example.com/",
            WebhookEndpoint = "https://notifications.internal.example.com/identity",
            WebhookApiKey = GeneratedSecret
        };

        IdentityConfigurationValidator.Validate(
            CreateAuthorizationOptions(),
            notifications,
            CreateEnvironment(Environments.Production));
    }

    [Fact]
    public void MigratorValidationDoesNotRequireRuntimeCertificatesOrNotificationSecrets()
    {
        var options = CreateAuthorizationOptions(
            issuer: null,
            signingPath: null,
            encryptionPath: null);

        IdentityConfigurationValidator.ValidateProvisioning(
            options,
            CreateEnvironment(Environments.Production));
    }

    [Fact]
    public void ProvisioningRejectsUnknownScopeAndCredentialOnPublicClient()
    {
        var options = CreateAuthorizationOptions();
        options.Clients[0] = new AuthorizationClientOptions
        {
            ClientId = "booking-web",
            DisplayName = "Booking web",
            Profile = AuthorizationClientProfile.Public,
            ClientSecret = GeneratedSecret,
            RedirectUris = ["https://app.example.com/callback"],
            Scopes = ["booking.delete"]
        };

        var exception = Assert.Throws<OptionsValidationException>(() =>
            IdentityConfigurationValidator.ValidateProvisioning(
                options,
                CreateEnvironment(Environments.Production)));

        Assert.Contains("unknown scope 'booking.delete'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("must not have client credentials", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceClientCannotUseUserOrRefreshCapabilities()
    {
        var options = CreateAuthorizationOptions();
        options.Clients[0] = new AuthorizationClientOptions
        {
            ClientId = "booking-worker",
            DisplayName = "Booking worker",
            Profile = AuthorizationClientProfile.Service,
            ClientSecret = GeneratedSecret,
            AllowRefreshTokens = true,
            Scopes = ["openid", "booking.read"]
        };

        var exception = Assert.Throws<OptionsValidationException>(() =>
            IdentityConfigurationValidator.ValidateProvisioning(
                options,
                CreateEnvironment(Environments.Production)));

        Assert.Contains("cannot enable refresh tokens", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot request user identity", exception.Message, StringComparison.Ordinal);
    }

    private static AuthorizationServerOptions CreateAuthorizationOptions(
        string? issuer = "https://identity.example.com/",
        string? signingPath = "/keys/signing.pfx",
        string? encryptionPath = "/keys/encryption.pfx") =>
        new()
        {
            Issuer = issuer,
            SigningCertificates = signingPath is null
                ? []
                : [new CertificateOptions { Path = signingPath }],
            EncryptionCertificates = encryptionPath is null
                ? []
                : [new CertificateOptions { Path = encryptionPath }],
            Scopes =
            [
                new AuthorizationScopeOptions
                {
                    Name = "booking.read",
                    DisplayName = "Read bookings",
                    Resource = "booking-public-api"
                }
            ],
            CorsOrigins = ["https://app.example.com"],
            Clients =
            [
                new AuthorizationClientOptions
                {
                    ClientId = "booking-web",
                    DisplayName = "Booking web",
                    Profile = AuthorizationClientProfile.Public,
                    RedirectUris = ["https://app.example.com/callback"],
                    Scopes = ["openid", "booking.read"]
                }
            ]
        };

    private static TestHostEnvironment CreateEnvironment(string name) =>
        new TestHostEnvironment
        {
            EnvironmentName = name,
            ApplicationName = "Identity.Api.Tests",
            ContentRootPath = AppContext.BaseDirectory,
            ContentRootFileProvider = new NullFileProvider()
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Identity.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
