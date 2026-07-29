using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microservices.Security;

public static class ApiSecurityExtensions
{
    private static readonly Action<ILogger, string, Exception?> LogAuthenticationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogAuthenticationFailure)),
            "JWT bearer authentication failed: {Reason}");

    private static readonly Action<ILogger, string, Exception?> LogClaimsValidationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogClaimsValidationFailure)),
            "The validated access token was rejected: {Reason}");

    public static IServiceCollection AddApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<IValidateOptions<ApiSecurityOptions>>(
            new ApiSecurityOptionsValidator(environment));
        services.AddOptions<ApiSecurityOptions>()
            .Bind(configuration.GetSection(ApiSecurityOptions.SectionName))
            .ValidateOnStart();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ApiSecurityOptions>, ILoggerFactory>(
                (jwt, configured, loggerFactory) =>
                    ConfigureJwtBearer(
                        jwt,
                        configured.Value,
                        environment,
                        loggerFactory.CreateLogger(
                            "Microservices.Security.JwtBearer")));

        var fallbackPolicy = new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(fallbackPolicy);
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider,
            ScopeAuthorizationPolicyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, ScopeAuthorizationHandler>());

        return services;
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions jwt,
        ApiSecurityOptions security,
        IHostEnvironment environment,
        ILogger logger)
    {
        jwt.Authority = security.Authority;
        jwt.Audience = security.Audience;
        if (!string.IsNullOrWhiteSpace(security.MetadataAddress))
        {
            jwt.MetadataAddress = security.MetadataAddress;
        }

        jwt.RequireHttpsMetadata = security.RequireHttpsMetadata;
        jwt.MapInboundClaims = false;
        jwt.SaveToken = false;
        jwt.IncludeErrorDetails = environment.IsDevelopment();
        jwt.RefreshOnIssuerKeyNotFound = true;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireAudience = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAudience = security.Audience,
            ValidTypes = security.ValidTokenTypes,
            ClockSkew = security.ClockSkew,
            NameClaimType = security.NameClaimType,
            RoleClaimType = SecurityClaimTypes.Role
        };

        jwt.Events ??= new JwtBearerEvents();
        var existingOnAuthenticationFailed = jwt.Events.OnAuthenticationFailed;
        jwt.Events.OnAuthenticationFailed = async context =>
        {
            await existingOnAuthenticationFailed(context);

            if (environment.IsDevelopment())
            {
                LogAuthenticationFailure(
                    logger,
                    context.Exception.Message,
                    context.Exception);
            }
        };

        var existingOnTokenValidated = jwt.Events.OnTokenValidated;
        jwt.Events.OnTokenValidated = async context =>
        {
            await existingOnTokenValidated(context);

            if (context.Principal is null)
            {
                return;
            }

            if (!AccessTokenClaimsValidator.TryValidate(
                    context.Principal,
                    security,
                    out var failure))
            {
                if (environment.IsDevelopment())
                {
                    LogClaimsValidationFailure(logger, failure!, null);
                }

                context.Fail(failure!);
                return;
            }

            KeycloakRoleClaimsMapper.MapRoles(context.Principal, security);
        };
    }
}
