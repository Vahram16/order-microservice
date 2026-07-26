using System.Security.Cryptography.X509Certificates;
using Identity.Api.Configuration;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Identity.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Security;

internal static class IdentitySecurityRegistration
{
    public static IdentityCertificateSet Add<TBuilder>(
        TBuilder builder,
        AuthorizationServerOptions authorizationServer)
        where TBuilder : IHostApplicationBuilder
    {
        AddIdentityCore(builder.Services);

        var certificates = LoadCertificates(
            authorizationServer,
            builder.Environment);
        AddDataProtection(
            builder.Services,
            builder.Environment,
            certificates.EncryptionCertificates);

        return certificates;
    }

    private static void AddIdentityCore(IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                options.Password.RequiredLength = 6;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<IdentityServiceDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "__Host-Identity.Session";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.IsEssential = true;
            cookie.Cookie.Path = "/";
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            cookie.LoginPath = "/account/login";
            cookie.AccessDeniedPath = "/account/access-denied";
            cookie.ReturnUrlParameter = "returnUrl";
            cookie.SlidingExpiration = true;
        });
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(5));
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(2));
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 220_000);
        services.AddSingleton<DummyPasswordVerifier>();
    }

    private static IdentityCertificateSet LoadCertificates(
        AuthorizationServerOptions authorizationServer,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return IdentityCertificateSet.Empty;
        }

        var signingCertificates = CertificateLoader.Load(
            authorizationServer.SigningCertificates,
            environment,
            TimeProvider.System);
        var encryptionCertificates = CertificateLoader.Load(
            authorizationServer.EncryptionCertificates,
            environment,
            TimeProvider.System);

        if (signingCertificates
            .Select(certificate => certificate.Thumbprint)
            .Intersect(
                encryptionCertificates.Select(certificate => certificate.Thumbprint),
                StringComparer.OrdinalIgnoreCase)
            .Any())
        {
            throw new InvalidOperationException(
                "OpenIddict signing and encryption certificates must use distinct keys.");
        }

        return new IdentityCertificateSet(
            signingCertificates,
            encryptionCertificates);
    }

    private static void AddDataProtection(
        IServiceCollection services,
        IHostEnvironment environment,
        IReadOnlyList<X509Certificate2> encryptionCertificates)
    {
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("microservices-identity")
            .PersistKeysToDbContext<IdentityServiceDbContext>();

        if (environment.IsDevelopment())
        {
            return;
        }

        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var activeEncryptionCertificate = encryptionCertificates.First(certificate =>
            certificate.NotBefore.ToUniversalTime() <= now &&
            certificate.NotAfter.ToUniversalTime() > now);
        dataProtection.ProtectKeysWithCertificate(activeEncryptionCertificate);
        dataProtection.UnprotectKeysWithAnyCertificate([.. encryptionCertificates]);
    }
}

internal sealed record IdentityCertificateSet(
    IReadOnlyList<X509Certificate2> SigningCertificates,
    IReadOnlyList<X509Certificate2> EncryptionCertificates)
{
    public static IdentityCertificateSet Empty { get; } = new([], []);
}
