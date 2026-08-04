using Microservices.Contracts;

namespace Microservices.Application.Messaging;

/// <summary>
/// Publishes integration events to every interested subscriber through the scoped transactional
/// bus outbox.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(
        TEvent message,
        IntegrationMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>
/// Sends one integration command to its single explicitly configured owning endpoint through the
/// scoped transactional bus outbox.
/// </summary>
public interface IIntegrationCommandSender<TCommand>
    where TCommand : class, IIntegrationCommand
{
    Task SendAsync(
        TCommand command,
        IntegrationMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional transport metadata shared by event publication and command sending. Business contracts
/// must not duplicate transport identity, correlation, causation, or broker headers.
/// </summary>
public sealed record IntegrationMessageMetadata(
    Guid? MessageId = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    IReadOnlyDictionary<string, string>? Headers = null);
