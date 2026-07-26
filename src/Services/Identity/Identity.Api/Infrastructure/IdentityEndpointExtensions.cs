using FluentValidation;
using Identity.Api.Configuration;
using Identity.Api.Features.Accounts;
using Identity.Api.Features.Authorization;
using Identity.Api.Features.Profile;
using Identity.Api.Model;
using Identity.Api.Security;
using MediatR;
using Microservices.Application;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Api.Infrastructure;

internal static class IdentityEndpointExtensions
{
    public static IServiceCollection AddIdentityApplication(
        this IServiceCollection services,
        IConfiguration applicationConfiguration)
    {
        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddMediatR(mediator =>
        {
            mediator.RegisterServicesFromAssemblyContaining<Program>();
            mediator.AddOpenBehavior(typeof(ValidationBehavior<,>));
            mediator.LicenseKey = applicationConfiguration["Licensing:MediatR"];
        });

        services.AddSingleton<IValidateOptions<IdentityInteractionOptions>,
            IdentityInteractionOptionsValidator>();
        services.AddOptions<IdentityInteractionOptions>()
            .Bind(applicationConfiguration.GetSection(
                IdentityInteractionOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<IdentityPasswordPolicyOptions>,
            IdentityPasswordPolicyOptionsValidator>();
        services.AddOptions<IdentityPasswordPolicyOptions>()
            .Bind(applicationConfiguration.GetSection(
                IdentityPasswordPolicyOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IdentityInteractionUrlBuilder>();
        services.AddSingleton<LogoutInteractionProtector>();
        services.AddSingleton<PasswordBlocklist>();
        services.AddScoped<IPasswordValidator<ApplicationUser>,
            BlockedPasswordValidator>();

        services.AddOpenIddict()
            .AddServer(options =>
            {
                options.EnableAuthorizationRequestCaching();
                options.EnableEndSessionRequestCaching();
            });

        ConfigureApplicationCookie(services);

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityApplication(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthorizationEndpoints();
        endpoints.MapAccountEndpoints();
        endpoints.MapProfileEndpoints();
        return endpoints;
    }

    private static void ConfigureApplicationCookie(IServiceCollection services)
    {
        services.PostConfigure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options =>
            {
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    var urls = context.HttpContext.RequestServices
                        .GetRequiredService<IdentityInteractionUrlBuilder>();
                    context.Response.Redirect(
                        urls.CreateLoginUri(context.Properties.RedirectUri));
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    var urls = context.HttpContext.RequestServices
                        .GetRequiredService<IdentityInteractionUrlBuilder>();
                    context.Response.Redirect(
                        urls.CreateAccessDeniedUri(context.Properties.RedirectUri));
                    return Task.CompletedTask;
                };
            });
    }
}
