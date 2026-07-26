using System.Threading.RateLimiting;
using FluentValidation;
using MediatR;
using Microservices.Application;
using Microservices.Persistence.Postgres;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;
using Notifications.Api.Delivery;
using Notifications.Api.Email;
using Notifications.Api.Email.Postmark;
using Notifications.Api.Persistence;
using Notifications.Api.Security;

namespace Notifications.Api.Infrastructure;

internal static class NotificationServiceExtensions
{
    public static WebApplicationBuilder AddNotificationService(
        this WebApplicationBuilder builder)
    {
        AddOptions(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddPostgresDbContext<NotificationDbContext>(
            builder.Configuration,
            "notifications-db",
            postgres => postgres.MigrationsHistoryTable(
                "__ef_migrations_history",
                "notifications"));
        builder.Services.AddDataProtection()
            .SetApplicationName("microservices-notifications")
            .PersistKeysToDbContext<NotificationDbContext>();

        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        builder.Services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<Program>();
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.LicenseKey = builder.Configuration["Licensing:MediatR"];
        });

        builder.Services.AddScoped<InternalApiKeyEndpointFilter>();
        builder.Services.AddScoped<NotificationDeliveryDispatcher>();
        builder.Services.AddHostedService<NotificationDeliveryWorker>();

        builder.Services.AddHttpClient<PostmarkEmailTransport>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<PostmarkOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseAddress, UriKind.Absolute);
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        builder.Services.AddScoped<IEmailTransport>(services =>
            services.GetRequiredService<PostmarkEmailTransport>());

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("notification-ingress", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 120,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));
        });

        return builder;
    }

    public static IApplicationBuilder UseNotificationSecurityHeaders(
        this IApplicationBuilder application) =>
        application.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
                context.Response.Headers.TryAdd("Cache-Control", "no-store");
                context.Response.Headers.TryAdd(
                    "Content-Security-Policy",
                    "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
                return Task.CompletedTask;
            });

            await next();
        });

    private static void AddOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<NotificationDeliveryOptions>,
            NotificationDeliveryOptionsValidator>();
        services.AddOptions<NotificationDeliveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<NotificationsIngressOptions>,
            NotificationsIngressOptionsValidator>();
        services.AddOptions<NotificationsIngressOptions>()
            .Bind(configuration.GetSection(NotificationsIngressOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PostmarkOptions>,
            PostmarkOptionsValidator>();
        services.AddOptions<PostmarkOptions>()
            .Bind(configuration.GetSection(PostmarkOptions.SectionName))
            .ValidateOnStart();
    }
}
