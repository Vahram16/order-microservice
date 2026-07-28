using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Microservices.Security;

public static class ApiSecurityExtensions
{
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
            .Configure<IOptions<ApiSecurityOptions>>((jwt, configured) =>
                ConfigureJwtBearer(jwt, configured.Value, environment));

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
        IHostEnvironment environment)
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
                context.Fail(failure!);
                return;
            }

            KeycloakRoleClaimsMapper.MapRoles(context.Principal, security);
        };
    }
}
