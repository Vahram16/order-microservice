using Microservices.Contracts;

namespace Microservices.Application.Messaging;

/// <summary>
/// The only production application publishing boundary. Implementations must use the scoped
/// transactional bus-outbox publish endpoint and apply transport metadata consistently.
/// </summary>
public interface IIntegrationMessagePublisher
{
    Task PublishAsync<TMessage>(
        TMessage message,
        IntegrationPublishMetadata? metadata = null,
        CancellationToken cancellationToken = default)
        where TMessage : class, IIntegrationMessage;
}

/// <summary>Optional transport metadata for publication. Payload contracts must not duplicate these values.</summary>
public sealed record IntegrationPublishMetadata(
    Guid? MessageId = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    IReadOnlyDictionary<string, string>? Headers = null);
