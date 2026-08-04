using System.Reflection;
using Customer.Api.Persistence;
using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts;
using Microservices.Messaging;
using ServiceTemplate.Api.Persistence;

namespace Microservices.ArchitectureTests;

public sealed class MessagingArchitectureTests
{
    private static readonly Assembly[] ApplicationAssemblies =
    [
        typeof(CustomerDbContext).Assembly,
        typeof(ServiceTemplateDbContext).Assembly,
        typeof(IIntegrationMessagePublisher).Assembly
    ];

    [Fact]
    public void ProductionApplicationCodeUsesOnlyApprovedPublishingBoundary()
    {
        var violations = MessagingDependencyRules.FindForbiddenDependencies(
            ApplicationAssemblies.SelectMany(SafeGetTypes));

        Assert.True(
            violations.Count == 0,
            "Production application code must publish through IIntegrationMessagePublisher. " +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ControllersDoNotAccessTransportInfrastructure()
    {
        var controllers = ApplicationAssemblies
            .SelectMany(SafeGetTypes)
            .Where(type =>
                type.Name.EndsWith("Controller", StringComparison.Ordinal) ||
                Inherits(type, "Microsoft.AspNetCore.Mvc.ControllerBase"));

        var violations = MessagingDependencyRules.FindForbiddenDependencies(controllers);

        Assert.True(
            violations.Count == 0,
            "Controllers must not access MassTransit or RabbitMQ directly. " +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DomainTypesDoNotDependOnMessagingPersistenceOrTransportInfrastructure()
    {
        var domainTypes = typeof(CustomerDbContext).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("Customer.Api.Domain", StringComparison.Ordinal) == true);
        var violations = MessagingDependencyRules.FindDependenciesWithPrefixes(
            domainTypes,
            [
                "MassTransit",
                "RabbitMQ.Client",
                "Microsoft.EntityFrameworkCore",
                "Microservices.Messaging"
            ],
            "Move the dependency behind an application interface; domain types must remain framework-free.");

        Assert.True(
            violations.Count == 0,
            "Domain types contain infrastructure dependencies:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ContractAssemblyDoesNotReferenceServiceInternalLayers()
    {
        var forbiddenAssemblyPrefixes = new[]
        {
            "Customer.",
            "ServiceTemplate.",
            "Microservices.Messaging",
            "Microsoft.EntityFrameworkCore",
            "MassTransit",
            "RabbitMQ.Client"
        };
        var references = typeof(IIntegrationMessage).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbiddenAssemblyPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            references.Length == 0,
            "Integration contracts must not reference domain, persistence, API, consumer, or transport layers. " +
            "Forbidden references: " + string.Join(", ", references));
    }

    [Fact]
    public void ApplicationInterfacesDoNotLeakInfrastructureTypes()
    {
        var publicInterfaces = typeof(IIntegrationMessagePublisher).Assembly
            .GetExportedTypes()
            .Where(type => type.IsInterface);
        var violations = MessagingDependencyRules.FindDependenciesWithPrefixes(
            publicInterfaces,
            ["MassTransit", "RabbitMQ.Client", "Microsoft.EntityFrameworkCore", "Microservices.Messaging"],
            "Expose an application-owned contract instead of an infrastructure type.");

        Assert.True(
            violations.Count == 0,
            "Application interfaces leak infrastructure details:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProductionAssembliesNeverReferenceTestHelpers()
    {
        var references = ApplicationAssemblies
            .Append(typeof(IIntegrationMessage).Assembly)
            .Append(typeof(RabbitMqMessagingExtensions).Assembly)
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Where(reference => reference.Name?.EndsWith(".Tests", StringComparison.Ordinal) == true)
            .Select(reference => reference.FullName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void NegativeFixtureReportsTypeDependencyAndApprovedAlternative()
    {
        var violations = MessagingDependencyRules.FindForbiddenDependencies(
            [typeof(InvalidDirectBusPublisher)]);

        var violation = Assert.Single(violations);
        Assert.Contains(typeof(InvalidDirectBusPublisher).FullName!, violation, StringComparison.Ordinal);
        Assert.Contains(typeof(IBus).FullName!, violation, StringComparison.Ordinal);
        Assert.Contains(nameof(IIntegrationMessagePublisher), violation, StringComparison.Ordinal);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static bool Inherits(Type type, string fullName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == fullName)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class InvalidDirectBusPublisher(IBus bus)
    {
        public IBus Bus { get; } = bus;
    }
}

internal static class MessagingDependencyRules
{
    private static readonly string[] ForbiddenTypeNames =
    [
        "MassTransit.IBus",
        "MassTransit.IBusControl",
        "MassTransit.ISendEndpointProvider",
        "MassTransit.RabbitMqTransport.IRabbitMqHostConfigurator",
        "MassTransit.RabbitMqTransport.IRabbitMqReceiveEndpointConfigurator"
    ];

    public static IReadOnlyList<string> FindForbiddenDependencies(IEnumerable<Type> types) =>
        FindDependencies(
            types,
            IsForbidden,
            "Use Microservices.Application.Messaging.IIntegrationMessagePublisher instead.");

    public static IReadOnlyList<string> FindDependenciesWithPrefixes(
        IEnumerable<Type> types,
        IReadOnlyCollection<string> forbiddenPrefixes,
        string approvedAlternative) =>
        FindDependencies(
            types,
            type => type.FullName is { } fullName &&
                forbiddenPrefixes.Any(prefix =>
                    fullName.StartsWith(prefix, StringComparison.Ordinal)),
            approvedAlternative);

    private static string[] FindDependencies(
        IEnumerable<Type> types,
        Func<Type, bool> isForbidden,
        string approvedAlternative)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var owner in types)
        {
            foreach (var dependency in DeclaredDependencies(owner))
            {
                foreach (var candidate in Expand(dependency))
                {
                    if (isForbidden(candidate))
                    {
                        violations.Add(
                            $"{owner.FullName} depends on forbidden {candidate.FullName}. {approvedAlternative}");
                    }
                }
            }
        }

        return violations.ToArray();
    }

    private static IEnumerable<Type> DeclaredDependencies(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var implementedInterface in type.GetInterfaces())
        {
            yield return implementedInterface;
        }

        foreach (var constructor in type.GetConstructors(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var field in type.GetFields(
                     BindingFlags.Instance | BindingFlags.Static |
                     BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(
                     BindingFlags.Instance | BindingFlags.Static |
                     BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(
                     BindingFlags.Instance | BindingFlags.Static |
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var expanded in Expand(elementType))
            {
                yield return expanded;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var expanded in Expand(argument))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static bool IsForbidden(Type type)
    {
        var fullName = type.FullName ?? string.Empty;
        return ForbiddenTypeNames.Contains(fullName, StringComparer.Ordinal) ||
               fullName.StartsWith("RabbitMQ.Client", StringComparison.Ordinal) ||
               (fullName.StartsWith("MassTransit.", StringComparison.Ordinal) &&
                fullName.Contains("RabbitMq", StringComparison.OrdinalIgnoreCase));
    }
}
