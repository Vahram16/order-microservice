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
            failures.Add($"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' must be an absolute RabbitMQ URI.");
        }
        else if (!IsRabbitMqScheme(hostAddress.Scheme))
        {
            failures.Add($"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' must use a RabbitMQ URI scheme.");
        }
        else if (hostAddress.Port == 0)
        {
            failures.Add($"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' cannot use port 0.");
        }
        else if (!IsSecureScheme(hostAddress.Scheme) && hostAddress.Port == 5671)
        {
            failures.Add($"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' cannot use an insecure URI scheme with TLS port 5671.");
        }
        else if (options.UseTls && !IsSecureScheme(hostAddress.Scheme))
        {
            failures.Add(
                $"Connection string '{RabbitMqMessagingOptions.ConnectionStringName}' must use an AMQPS URI when " +
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.UseTls)}' is enabled.");
        }

        ValidatePositive(options.OutboxQueryDelay, nameof(options.OutboxQueryDelay), failures);
        ValidatePositive(options.DuplicateDetectionWindow, nameof(options.DuplicateDetectionWindow), failures);
        if (options.DuplicateDetectionWindow < options.OutboxQueryDelay)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.DuplicateDetectionWindow)}' must be " +
                $"greater than or equal to '{nameof(RabbitMqMessagingOptions.OutboxQueryDelay)}'.");
        }

        ValidateIntervals(options.RetryIntervals, nameof(options.RetryIntervals), TimeSpan.FromSeconds(30), failures);
        ValidateIntervals(options.RedeliveryIntervals, nameof(options.RedeliveryIntervals), TimeSpan.FromDays(1), failures);
        ValidatePositive(options.StartTimeout, nameof(options.StartTimeout), failures);
        ValidatePositive(options.StopTimeout, nameof(options.StopTimeout), failures);
        ValidatePositive(options.ConsumerStopTimeout, nameof(options.ConsumerStopTimeout), failures);

        if (options.ConsumerStopTimeout > options.StopTimeout)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.ConsumerStopTimeout)}' must not exceed '{nameof(RabbitMqMessagingOptions.StopTimeout)}'.");
        }

        if (options.PrefetchCount == 0)
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.PrefetchCount)}' must be greater than zero.");
        }

        if (options.ConcurrentMessageLimit == 0 || options.ConcurrentMessageLimit > options.PrefetchCount)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.ConcurrentMessageLimit)}' must be between 1 and PrefetchCount.");
        }

        foreach (var consumer in options.Consumers)
        {
            ValidateConsumerPolicy(consumer.Key, consumer.Value, failures);
        }

        if (options.Port is 0)
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.Port)}' must be greater than 0 when configured.");
        }
        else if (!options.UseTls && options.Port is 5671)
        {
            failures.Add(
                $"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.Port)}' cannot be 5671 when " +
                $"'{nameof(RabbitMqMessagingOptionsOptions.UseTls)}' is disabled.");
        }

        if (options.TlsServerName is not null && string.IsNullOrWhiteSpace(options.TlsServerName))
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{nameof(RabbitMqMessagingOptions.TlsServerName)}' cannot be blank when configured.");
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

    private static void ValidateConsumerPolicy(
        string endpointName,
        ConsumerFailurePolicyOptions policy,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:Consumers' cannot contain a blank endpoint name.");
            return;
        }

        var prefix = $"{nameof(RabbitMqMessagingOptions.Consumers)}:{endpointName}";
        if (policy.RetryIntervals is not null)
        {
            ValidateIntervals(policy.RetryIntervals, $"{prefix}:{nameof(policy.RetryIntervals)}", TimeSpan.FromSeconds(30), failures);
        }

        if (policy.RedeliveryIntervals is not null)
        {
            ValidateIntervals(policy.RedeliveryIntervals, $"{prefix}:{nameof(policy.RedeliveryIntervals)}", TimeSpan.FromDays(1), failures);
        }

        var prefetch = policy.PrefetchCount ?? 1;
        if (policy.PrefetchCount is 0)
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{prefix}:{nameof(policy.PrefetchCount)}' must be greater than zero.");
        }

        if (policy.ConcurrentMessageLimit is 0 || policy.ConcurrentMessageLimit > prefetch)
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{prefix}:{nameof(policy.ConcurrentMessageLimit)}' must be between 1 and PrefetchCount.");
        }
    }

    private static void ValidateIntervals(
        TimeSpan[]? intervals,
        string propertyName,
        TimeSpan maximumInterval,
        List<string> failures)
    {
        if (intervals is null || intervals.Length == 0)
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{propertyName}' must contain at least one interval.");
            return;
        }

        if (intervals.Length > 10 || intervals.Any(interval => interval <= TimeSpan.Zero || interval > maximumInterval))
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{propertyName}' must contain 1-10 positive, bounded intervals.");
        }
    }

    private static void ValidatePositive(TimeSpan value, string propertyName, List<string> failures)
    {
        if (value <= TimeSpan.Zero)
        {
            failures.Add($"'{RabbitMqMessagingOptions.SectionName}:{propertyName}' must be positive.");
        }
    }

    private static void ValidateHostOptions(RabbitMqMessagingOptions options, List<string> failures)
    {
        AddRequiredFailure(options.Host, nameof(RabbitMqMessagingOptions.Host), failures);
        AddRequiredFailure(options.VirtualHost, nameof(RabbitMqMessagingOptions.VirtualHost), failures);
        AddRequiredFailure(options.Username, nameof(RabbitMqMessagingOptions.Username), failures);
        AddRequiredFailure(options.Password, nameof(RabbitMqMessagingOptions.Password), failures);
    }

    private static void AddRequiredFailure(string value, string propertyName, List<string> failures)
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
