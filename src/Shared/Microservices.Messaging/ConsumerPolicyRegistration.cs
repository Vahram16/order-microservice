using System.Runtime.CompilerServices;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Microservices.Messaging;

/// <summary>
/// Registers a consumer with a stable broker endpoint identity and an explicit delivery policy.
/// The endpoint name is topology, not a formatted CLR type name.
/// </summary>
public static class GovernedConsumerRegistrationExtensions
{
    public static IConsumerRegistrationConfigurator<TConsumer> AddConsumerWithPolicy<TConsumer>(
        this IBusRegistrationConfigurator configurator,
        string endpointName,
        ConsumerDeliveryPolicyOptions policy)
        where TConsumer : class, IConsumer
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(policy);

        var registry = ConsumerPolicyRegistryStore.Get(configurator);
        registry.Add(typeof(TConsumer), endpointName, policy);

        var consumer = configurator.AddConsumer<TConsumer>();
        consumer.Endpoint(endpoint => endpoint.Name = endpointName);
        return consumer;
    }
}

internal sealed class ConsumerPolicyRegistry(
    RabbitMqMessagingOptions globalOptions)
{
    private readonly Dictionary<string, TypedConsumerPolicy> _typedPolicies =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _matchedEndpoints = new(StringComparer.Ordinal);

    public void Add(
        Type consumerType,
        string endpointName,
        ConsumerDeliveryPolicyOptions policy)
    {
        if (!RabbitMqMessagingOptionsValidator.IsValidEndpointName(endpointName))
        {
            throw new OptionsValidationException(
                RabbitMqMessagingOptions.SectionName,
                typeof(RabbitMqMessagingOptions),
                [$"Consumer '{consumerType.FullName}' declares invalid endpoint name '{endpointName}'. Use stable lowercase kebab-case text."]);
        }

        var failures = new List<string>();
        RabbitMqMessagingOptionsValidator.ValidateConsumerPolicy(
            endpointName,
            policy,
            globalOptions,
            failures);
        if (failures.Count != 0)
        {
            throw new OptionsValidationException(
                RabbitMqMessagingOptions.SectionName,
                typeof(RabbitMqMessagingOptions),
                failures.Select(failure => $"Consumer '{consumerType.FullName}': {failure}"));
        }

        if (!_typedPolicies.TryAdd(endpointName, new TypedConsumerPolicy(consumerType, policy)))
        {
            var existing = _typedPolicies[endpointName];
            throw new OptionsValidationException(
                RabbitMqMessagingOptions.SectionName,
                typeof(RabbitMqMessagingOptions),
                [
                    $"Consumers '{existing.ConsumerType.FullName}' and '{consumerType.FullName}' both resolve to endpoint '{endpointName}'. " +
                    "Assign distinct stable topology names."
                ]);
        }
    }

    public ConsumerDeliveryPolicyOptions Resolve(string endpointName)
    {
        _matchedEndpoints.Add(endpointName);

        if (_typedPolicies.TryGetValue(endpointName, out var typed))
        {
            return typed.Policy;
        }

        if (globalOptions.Consumers.TryGetValue(endpointName, out var configured))
        {
            return configured;
        }

        if (globalOptions.AllowValidatedDefaultConsumerPolicy)
        {
            return new ConsumerDeliveryPolicyOptions();
        }

        throw new OptionsValidationException(
            RabbitMqMessagingOptions.SectionName,
            typeof(RabbitMqMessagingOptions),
            [
                $"Endpoint '{endpointName}' has no explicit consumer policy. Register the consumer with " +
                "AddConsumerWithPolicy<TConsumer>(endpointName, policy), add a validated legacy Consumers entry, " +
                "or explicitly approve the global default."
            ]);
    }

    public void ValidateAllPoliciesMatched()
    {
        var failures = new List<string>();

        foreach (var typed in _typedPolicies)
        {
            if (!_matchedEndpoints.Contains(typed.Key))
            {
                failures.Add(
                    $"Consumer '{typed.Value.ConsumerType.FullName}' declares endpoint policy '{typed.Key}', but no receive endpoint matched it.");
            }
        }

        foreach (var configured in globalOptions.Consumers.Keys)
        {
            if (!_matchedEndpoints.Contains(configured))
            {
                failures.Add(
                    $"Configured consumer policy '{configured}' matches no registered receive endpoint. Remove the stale key or deploy the matching endpoint.");
            }
        }

        if (failures.Count != 0)
        {
            throw new OptionsValidationException(
                RabbitMqMessagingOptions.SectionName,
                typeof(RabbitMqMessagingOptions),
                failures);
        }
    }

    private sealed record TypedConsumerPolicy(
        Type ConsumerType,
        ConsumerDeliveryPolicyOptions Policy);
}

internal static class ConsumerPolicyRegistryStore
{
    private static readonly ConditionalWeakTable<IBusRegistrationConfigurator, ConsumerPolicyRegistry>
        Registries = new();

    public static void Attach(
        IBusRegistrationConfigurator configurator,
        ConsumerPolicyRegistry registry)
    {
        Registries.Add(configurator, registry);
    }

    public static ConsumerPolicyRegistry Get(IBusRegistrationConfigurator configurator) =>
        Registries.TryGetValue(configurator, out var registry)
            ? registry
            : throw new InvalidOperationException(
                "AddConsumerWithPolicy must be called from the registration callback supplied to AddRabbitMqWithPostgresOutbox.");
}
