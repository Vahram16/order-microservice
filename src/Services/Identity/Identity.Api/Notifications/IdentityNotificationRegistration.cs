using Identity.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

internal static class IdentityNotificationRegistration
{
    public static void Add(
        IServiceCollection services,
        IConfiguration configuration,
        IdentityNotificationOptions options)
    {
        services.Configure<IdentityNotificationOptions>(
            configuration.GetSection(IdentityNotificationOptions.SectionName));

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
                services.AddScoped<IdentityNotificationOutboxDispatcher>();
                services.AddHostedService<IdentityNotificationOutboxWorker>();
                break;

            default:
                throw new OptionsValidationException(
                    IdentityNotificationOptions.SectionName,
                    typeof(IdentityNotificationOptions),
                    ["An identity notification provider is required."]);
        }
    }
}
