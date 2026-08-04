namespace Microservices.Contracts;

/// <summary>
/// A fact owned by the bounded context in which it occurred. The publishing bounded context owns
/// the contract. Transport identity, correlation, and causation belong in headers.
/// </summary>
public interface IIntegrationEvent : IIntegrationMessage
{
    DateTimeOffset OccurredAtUtc { get; }
}
