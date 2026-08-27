using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Webhooks;

namespace Payment.Api.Infrastructure.Stripe;

internal static class StripeServiceCollectionExtensions
{
    public static IServiceCollection AddStripePayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "Stripe SecretKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.WebhookSecret), "Stripe WebhookSecret is required.")
            .ValidateOnStart();

        services.AddScoped<StripePaymentProvider>();
        services.AddScoped<IPaymentProvider>(provider => provider.GetRequiredService<StripePaymentProvider>());
        services.AddScoped<IOrderPaymentProvider>(provider => provider.GetRequiredService<StripePaymentProvider>());
        services.AddSingleton<IPaymentWebhookVerifier, StripeWebhookVerifier>();
        return services;
    }
}
