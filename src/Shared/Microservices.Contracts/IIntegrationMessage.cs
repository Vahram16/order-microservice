namespace Microservices.Contracts;

/// <summary>
/// Canonical marker for a contract published between bounded contexts.
/// Transport identity, correlation, causation, retry state, and broker headers are not payload data.
/// </summary>
public interface IIntegrationMessage;

/// <summary>
/// A fact owned by the bounded context in which it occurred. The publisher owns the contract.
/// </summary>
public interface IIntegrationEvent : IIntegrationMessage
{
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>
/// A request for one owning bounded context to perform an action. Commands must have one logical owner.
/// </summary>
public interface IIntegrationCommand : IIntegrationMessage;
