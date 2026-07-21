using Microsoft.Extensions.Options;

namespace Microservices.Messaging;

internal static class RabbitMqMessagingOptionsValidator
{
    private const string AmqpsScheme = "amqps";
    private const string AmqpScheme = "amqp";
    private const string RabbitMqScheme = "rabbitmq";
    private const string RabbitMqSecureScheme = "rabbitmqs";

    public static Uri? ValidateAndGetHostAddress(
        RabbitMqMessagingOptions options,
        string? connectionString)
    {
        var failures = new List<string>();
        Uri? hostAddress = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ValidateHostOptions(options, failures);
        }
        else if (!Uri.TryCreate(connectionString, UriKind.Absolute, out hostAddress) ||
                 string.IsNullOrWhiteSpace(hostAddress.Host))
        {
            failures.Add(
                $"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' must be an absolute RabbitMQ URI.");
        }
        else if (!IsRabbitMqScheme(hostAddress.Scheme))
        {
            failures.Add(
                $"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' must use a RabbitMQ URI scheme.");
        }
        else if (hostAddress.Port == 0)
        {
            failures.Add(
                $"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' cannot use port 0.");
        }
        else if (!IsSecureScheme(hostAddress.Scheme) && hostAddress.Port == 5671)
        {
            failures.Add(
                $"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' cannot use an insecure URI scheme with TLS port 5671.");
        }
        else if (options.UseTls && !IsSecureScheme(hostAddress.Scheme))
        {
            failures.Add(
                $"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' must use an AMQPS URI when " +
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.UseTls)}' is enabled.");
        }

        if (options.OutboxQueryDelay <= TimeSpan.Zero)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.OutboxQueryDelay)}' must be positive.");
        }

        if (options.DuplicateDetectionWindow <= TimeSpan.Zero)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.DuplicateDetectionWindow)}' must be positive.");
        }
        else if (options.DuplicateDetectionWindow < options.OutboxQueryDelay)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.DuplicateDetectionWindow)}' must be " +
                $"greater than or equal to '{nameof(RabbitMqMessagingOptions.OutboxQueryDelay)}'.");
        }

        if (options.Port is 0)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.Port)}' must be greater than 0 when configured.");
        }
        else if (!options.UseTls && options.Port is 5671)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.Port)}' cannot be 5671 when " +
                $"'{nameof(RabbitMqMessagingOptions.UseTls)}' is disabled.");
        }

        if (options.TlsServerName is not null &&
            string.IsNullOrWhiteSpace(options.TlsServerName))
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.TlsServerName)}' cannot be blank when configured.");
        }

        if (failures.Count != 0)
        {
            throw new OptionsValidationException(
                RabbitMqMessagingOptions.SectionName,
                typeof(RabbitMqMessagingOptions),
                failures);
        }

        return hostAddress;
    }

    private static void ValidateHostOptions(
        RabbitMqMessagingOptions options,
        ICollection<string> failures)
    {
        AddRequiredFailure(options.Host, nameof(RabbitMqMessagingOptions.Host), failures);
        AddRequiredFailure(options.VirtualHost, nameof(RabbitMqMessagingOptions.VirtualHost), failures);
        AddRequiredFailure(options.Username, nameof(RabbitMqMessagingOptions.Username), failures);
        AddRequiredFailure(options.Password, nameof(RabbitMqMessagingOptions.Password), failures);
    }

    private static void AddRequiredFailure(
        string value,
        string propertyName,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{propertyName}' is required.");
        }
    }

    internal static bool IsSecureScheme(string scheme) =>
        scheme.Equals(AmqpsScheme, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(RabbitMqSecureScheme, StringComparison.OrdinalIgnoreCase);

    private static bool IsRabbitMqScheme(string scheme) =>
        scheme.Equals(AmqpScheme, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(AmqpsScheme, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(RabbitMqScheme, StringComparison.OrdinalIgnoreCase) ||
        scheme.Equals(RabbitMqSecureScheme, StringComparison.OrdinalIgnoreCase);
}
