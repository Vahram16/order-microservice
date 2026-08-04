namespace Microservices.Contracts;

/// <summary>
/// Canonical marker for a contract published between bounded contexts.
/// Transport identity, correlation, causation, retry state, and broker headers are not payload data.
/// </summary>
public interface IIntegrationMessage;

/// <summary>
/// A request for one owning bounded context to perform an action. Commands must have one logical owner.
/// </summary>
public interface IIntegrationCommand : IIntegrationMessage;
