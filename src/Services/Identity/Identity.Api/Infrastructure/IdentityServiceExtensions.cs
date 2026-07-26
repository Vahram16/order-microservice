using System.Threading.RateLimiting;
using Identity.Api.Configuration;
using Identity.Api.Maintenance;
using Identity.Api.Model;
using Identity.Api.Notifications;
using Identity.Api.Persistence;
using Microservices.Persistence.Postgres;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Infrastructure;

public static class IdentityServiceExtensions
{
    public const string AccountRateLimitPolicy = "identity-account";
    public const string LoginRateLimitPolicy = "identity-login";
    public const string TokenRateLimitPolicy = "identity-token";
    public const string BrowserCorsPolicy = "identity-browser";
    public const string ProfilePolicy = "identity-profile";
    private const string IdentityApiAudience = "identity-api";

    public static TBuilder AddIdentityService<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var authorizationServer = builder.Configuration
            .GetSection(AuthorizationServerOptions.SectionName)
            .Get<AuthorizationServerOptions>() ?? new AuthorizationServerOptions();
        var notifications = builder.Configuration
            .GetSection(IdentityNotificationOptions.SectionName)
            .Get<IdentityNotificationOptions>() ?? new IdentityNotificationOptions();

        IdentityConfigurationValidator.Validate(
            authorizationServer,
            notifications,
            builder.Environment);

        builder.Services.Configure<AuthorizationServerOptions>(
            builder.Configuration.GetSection(AuthorizationServerOptions.SectionName));
        builder.Services.Configure<IdentityNotificationOptions>(
            builder.Configuration.GetSection(IdentityNotificationOptions.SectionName));
        builder.Services.AddSingleton<IValidateOptions<IdentityMaintenanceOptions>,
            IdentityMaintenanceOptionsValidator>();
        builder.Services.AddOptions<IdentityMaintenanceOptions>()
            .Bind(builder.Configuration.GetSection(IdentityMaintenanceOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton(TimeProvider.System);
        AddPersistence(builder);

        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
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

        builder.Services.ConfigureApplicationCookie(cookie =>
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
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(5));
        builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(2));
        builder.Services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 220_000);
        builder.Services.AddSingleton<DummyPasswordVerifier>();

        var signingCertificates = builder.Environment.IsDevelopment()
            ? []
            : CertificateLoader.Load(
                authorizationServer.SigningCertificates,
                builder.Environment,
                TimeProvider.System);
        var encryptionCertificates = builder.Environment.IsDevelopment()
            ? []
            : CertificateLoader.Load(
                authorizationServer.EncryptionCertificates,
                builder.Environment,
                TimeProvider.System);
        if (!builder.Environment.IsDevelopment() && signingCertificates
            .Select(certificate => certificate.Thumbprint)
            .Intersect(
                encryptionCertificates.Select(certificate => certificate.Thumbprint),
                StringComparer.OrdinalIgnoreCase)
            .Any())
        {
            throw new InvalidOperationException(
                "OpenIddict signing and encryption certificates must use distinct keys.");
        }

        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("microservices-identity")
            .PersistKeysToDbContext<IdentityServiceDbContext>();
        if (!builder.Environment.IsDevelopment())
        {
            var now = TimeProvider.System.GetUtcNow().UtcDateTime;
            var activeEncryptionCertificate = encryptionCertificates.First(certificate =>
                certificate.NotBefore.ToUniversalTime() <= now &&
                certificate.NotAfter.ToUniversalTime() > now);
            dataProtection.ProtectKeysWithCertificate(activeEncryptionCertificate);
            dataProtection.UnprotectKeysWithAnyCertificate([.. encryptionCertificates]);
        }

        builder.Services.AddOpenIddict()
            .AddServer(options =>
            {
                if (!string.IsNullOrWhiteSpace(authorizationServer.Issuer))
                {
                    options.SetIssuer(new Uri(authorizationServer.Issuer, UriKind.Absolute));
                }

                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetPushedAuthorizationEndpointUris("/connect/par")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetTokenEndpointUris("/connect/token")
                    .SetRevocationEndpointUris("/connect/revocation")
                    .SetUserInfoEndpointUris("/connect/userinfo");

                options.RegisterScopes(
                    [
                        Scopes.Email,
                        Scopes.Profile,
                        Scopes.Roles,
                        .. authorizationServer.Scopes.Select(scope => scope.Name)
                    ]);
                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow();
                options.RequireProofKeyForCodeExchange();
                options.Configure(server =>
                {
                    server.ClientAuthenticationMethods.Clear();
                    server.ClientAuthenticationMethods.Add(
                        ClientAuthenticationMethods.ClientSecretBasic);
                    server.ClientAuthenticationMethods.Add(
                        ClientAuthenticationMethods.PrivateKeyJwt);
                    server.CodeChallengeMethods.Clear();
                    server.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                    server.PromptValues.Remove(PromptValues.Consent);
                    server.PromptValues.Remove(PromptValues.SelectAccount);
                    server.ResponseModes.Remove(ResponseModes.Fragment);
                });

                options.SetAuthorizationCodeLifetime(
                        authorizationServer.AuthorizationCodeLifetime)
                    .SetAccessTokenLifetime(authorizationServer.AccessTokenLifetime)
                    .SetIdentityTokenLifetime(authorizationServer.IdentityTokenLifetime)
                    .SetRefreshTokenLifetime(authorizationServer.RefreshTokenLifetime)
                    .SetRefreshTokenReuseLeeway(TimeSpan.Zero);

                options.UseReferenceRefreshTokens();
                options.DisableAccessTokenEncryption();

                if (builder.Environment.IsDevelopment())
                {
                    if (authorizationServer.UseEphemeralKeysInDevelopment)
                    {
                        options.AddEphemeralEncryptionKey()
                            .AddEphemeralSigningKey();
                    }
                    else
                    {
                        options.AddDevelopmentEncryptionCertificate()
                            .AddDevelopmentSigningCertificate();
                    }
                }
                else
                {
                    foreach (var certificate in signingCertificates)
                    {
                        options.AddSigningCertificate(certificate);
                    }

                    foreach (var certificate in encryptionCertificates)
                    {
                        options.AddEncryptionCertificate(certificate);
                    }
                }

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.AddAudiences(IdentityApiAudience);
                options.UseAspNetCore();
            });

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(ProfilePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.HasScope("identity.profile.read"));
            });

        AddNotificationSender(builder.Services, notifications);
        AddCors(builder.Services, authorizationServer);
        AddRateLimiting(builder.Services);
        AddMaintenance(builder.Services);

        return builder;
    }

    public static TBuilder AddIdentityPersistence<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var authorizationServer = builder.Configuration
            .GetSection(AuthorizationServerOptions.SectionName)
            .Get<AuthorizationServerOptions>() ?? new AuthorizationServerOptions();
        IdentityConfigurationValidator.ValidateProvisioning(
            authorizationServer,
            builder.Environment);

        builder.Services.Configure<AuthorizationServerOptions>(
            builder.Configuration.GetSection(AuthorizationServerOptions.SectionName));
        AddPersistence(builder);
        return builder;
    }

    public static IApplicationBuilder UseIdentitySecurityHeaders(
        this IApplicationBuilder application) =>
        application.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
                context.Response.Headers.TryAdd(
                    "Permissions-Policy",
                    "camera=(), microphone=(), geolocation=()");
                context.Response.Headers.TryAdd(
                    "Content-Security-Policy",
                    "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'");

                if (context.Request.Path.StartsWithSegments("/connect") ||
                    context.Request.Path.StartsWithSegments("/account"))
                {
                    context.Response.Headers.CacheControl = "no-store, no-cache";
                    context.Response.Headers.Pragma = "no-cache";
                }

                return Task.CompletedTask;
            });

            await next();
        });

    private static void AddNotificationSender(
        IServiceCollection services,
        IdentityNotificationOptions options)
    {
        switch (options.Provider)
        {
            case IdentityNotificationProvider.DevelopmentLog:
                services.AddScoped<IIdentityNotificationSender,
                    DevelopmentIdentityNotificationSender>();
                break;
            case IdentityNotificationProvider.Webhook:
                services.AddHttpClient<WebhookIdentityNotificationTransport>(client =>
                    client.Timeout = TimeSpan.FromSeconds(15));
                services.AddScoped<IIdentityNotificationTransport>(provider =>
                    provider.GetRequiredService<WebhookIdentityNotificationTransport>());
                services.AddScoped<IIdentityNotificationSender,
                    OutboxIdentityNotificationSender>();
                services.AddHostedService<IdentityNotificationOutboxWorker>();
                break;
            default:
                throw new OptionsValidationException(
                    IdentityNotificationOptions.SectionName,
                    typeof(IdentityNotificationOptions),
                    ["An identity notification provider is required."]);
        }
    }

    private static void AddRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                CreateProtocolRateLimitPartition);
            options.AddPolicy(AccountRateLimitPolicy, context =>
                CreateFixedWindowPartition(context, 5, TimeSpan.FromMinutes(1)));
            options.AddPolicy(LoginRateLimitPolicy, context =>
                CreateFixedWindowPartition(context, 10, TimeSpan.FromMinutes(5)));
            options.AddPolicy(TokenRateLimitPolicy, context =>
                CreateFixedWindowPartition(context, 60, TimeSpan.FromMinutes(1)));
        });
    }

    private static void AddMaintenance(IServiceCollection services)
    {
        services.AddScoped<IOpenIddictPruner, OpenIddictPruner>();
        services.AddScoped<OpenIddictPruningOperation>();
        services.AddHostedService<OpenIddictMaintenanceService>();
    }

    private static void AddCors(
        IServiceCollection services,
        AuthorizationServerOptions authorizationServer)
    {
        var origins = authorizationServer.CorsOrigins
            .Select(value => new Uri(value, UriKind.Absolute))
            .Select(uri => uri.GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        services.AddCors(options => options.AddPolicy(BrowserCorsPolicy, policy =>
        {
            if (origins.Length == 0)
            {
                policy.SetIsOriginAllowed(_ => false);
            }
            else
            {
                policy.WithOrigins(origins);
            }

            policy.WithHeaders("Authorization", "Content-Type")
                .WithMethods("GET", "POST", "OPTIONS");
        }));
    }

    private static void AddPersistence<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddPostgresDbContext<IdentityServiceDbContext>(
            builder.Configuration,
            "identity-db",
            options => options.UseOpenIddict<Guid>(),
            postgres => postgres.MigrationsHistoryTable(
                "__ef_migrations_history",
                "identity"));

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityServiceDbContext>()
                    .ReplaceDefaultEntities<Guid>();
            });
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(
        HttpContext context,
        int permitLimit,
        TimeSpan window) =>
        CreateFixedWindowPartition(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            permitLimit,
            window);

    private static RateLimitPartition<string> CreateProtocolRateLimitPartition(
        HttpContext context)
    {
        var endpoint = context.Request.Path.Value switch
        {
            var path when string.Equals(
                path,
                "/connect/authorize",
                StringComparison.OrdinalIgnoreCase) => (Name: "authorize", Limit: 60),
            var path when string.Equals(
                path,
                "/connect/par",
                StringComparison.OrdinalIgnoreCase) => (Name: "par", Limit: 30),
            var path when string.Equals(
                path,
                "/connect/revocation",
                StringComparison.OrdinalIgnoreCase) => (Name: "revocation", Limit: 30),
            var path when string.Equals(
                path,
                "/connect/userinfo",
                StringComparison.OrdinalIgnoreCase) => (Name: "userinfo", Limit: 120),
            var path when string.Equals(
                path,
                "/connect/logout",
                StringComparison.OrdinalIgnoreCase) => (Name: "logout", Limit: 30),
            _ => default
        };

        if (endpoint == default)
        {
            return RateLimitPartition.GetNoLimiter("identity-unrestricted");
        }

        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return CreateFixedWindowPartition(
            $"protocol:{endpoint.Name}:{address}",
            endpoint.Limit,
            TimeSpan.FromMinutes(1));
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(
        string partitionKey,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = window
            });
}
