using Identity.Api.Configuration;
using Identity.Api.Features.Authorization;
using Identity.Api.Maintenance;
using Identity.Api.Notifications;
using Identity.Api.Persistence;
using Identity.Api.Security;

namespace Identity.Api.Infrastructure;

public static class IdentityServiceExtensions
{
    public const string AccountRateLimitPolicy = "identity-account";
    public const string LoginRateLimitPolicy = "identity-login";
    public const string TokenRateLimitPolicy = "identity-token";
    public const string BrowserCorsPolicy = "identity-browser";
    public const string ProfilePolicy = "identity-profile";

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
        builder.Services.AddSingleton(TimeProvider.System);

        IdentityPersistenceRegistration.Add(builder);
        var certificates = IdentitySecurityRegistration.Add(
            builder,
            authorizationServer);
        IdentityAuthorizationRegistration.Add(
            builder,
            authorizationServer,
            certificates);
        IdentityNotificationRegistration.Add(
            builder.Services,
            builder.Configuration,
            notifications);
        IdentityMaintenanceRegistration.Add(
            builder.Services,
            builder.Configuration);

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
        IdentityPersistenceRegistration.Add(builder);

        return builder;
    }
}
