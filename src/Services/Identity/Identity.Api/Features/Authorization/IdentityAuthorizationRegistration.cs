using System.Threading.RateLimiting;
using Identity.Api.Configuration;
using Identity.Api.Infrastructure;
using Identity.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Identity.Api.Features.Authorization;

internal static class IdentityAuthorizationRegistration
{
    private const string IdentityApiAudience = "identity-api";

    public static void Add<TBuilder>(
        TBuilder builder,
        AuthorizationServerOptions authorizationServer,
        IdentityCertificateSet certificates)
        where TBuilder : IHostApplicationBuilder
    {
        AddOpenIddict(builder, authorizationServer, certificates);
        AddAuthorizationPolicies(builder.Services);
        AddBrowserCors(builder.Services, authorizationServer);
        AddRateLimiting(builder.Services);
    }

    private static void AddOpenIddict<TBuilder>(
        TBuilder builder,
        AuthorizationServerOptions authorizationServer,
        IdentityCertificateSet certificates)
        where TBuilder : IHostApplicationBuilder
    {
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
                    foreach (var certificate in certificates.SigningCertificates)
                    {
                        options.AddSigningCertificate(certificate);
                    }

                    foreach (var certificate in certificates.EncryptionCertificates)
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
    }

    private static void AddAuthorizationPolicies(IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(IdentityServiceExtensions.ProfilePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.HasScope("identity.profile.read"));
            });
    }

    private static void AddBrowserCors(
        IServiceCollection services,
        AuthorizationServerOptions authorizationServer)
    {
        var origins = authorizationServer.CorsOrigins
            .Select(value => new Uri(value, UriKind.Absolute))
            .Select(uri => uri.GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        services.AddCors(options => options.AddPolicy(
            IdentityServiceExtensions.BrowserCorsPolicy,
            policy =>
            {
                if (origins.Length == 0)
                {
                    policy.SetIsOriginAllowed(_ => false);
                }
                else
                {
                    policy.WithOrigins(origins);
                }

                policy.AllowCredentials()
                    .WithHeaders("Authorization", "Content-Type")
                    .WithMethods("GET", "POST", "OPTIONS")
                    .SetPreflightMaxAge(TimeSpan.FromHours(1));
            }));
    }

    private static void AddRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                CreateProtocolRateLimitPartition);
            options.AddPolicy(IdentityServiceExtensions.AccountRateLimitPolicy, context =>
                CreateFixedWindowPartition(context, 5, TimeSpan.FromMinutes(1)));
            options.AddPolicy(IdentityServiceExtensions.LoginRateLimitPolicy, context =>
                CreateFixedWindowPartition(context, 10, TimeSpan.FromMinutes(5)));
            options.AddPolicy(IdentityServiceExtensions.TokenRateLimitPolicy, context =>
                CreateFixedWindowPartition(context, 60, TimeSpan.FromMinutes(1)));
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
