using Microsoft.Extensions.Configuration;

namespace Microservices.Messaging;

internal static class RabbitMqMessagingOptionsBinder
{
    public static RabbitMqMessagingOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(RabbitMqMessagingOptions.SectionName);
        var options = section.Get<RabbitMqMessagingOptions>() ?? new RabbitMqMessagingOptions();

        ReplaceConfiguredArray(
            section,
            nameof(RabbitMqMessagingOptions.RetryIntervals),
            intervals => options.RetryIntervals = intervals);
        ReplaceConfiguredArray(
            section,
            nameof(RabbitMqMessagingOptions.RedeliveryIntervals),
            intervals => options.RedeliveryIntervals = intervals);

        return options;
    }

    private static void ReplaceConfiguredArray(
        IConfigurationSection messagingSection,
        string propertyName,
        Action<TimeSpan[]> replace)
    {
        var intervalSection = messagingSection.GetSection(propertyName);
        if (!intervalSection.GetChildren().Any())
        {
            return;
        }

        replace(intervalSection.Get<TimeSpan[]>() ?? []);
    }
}
