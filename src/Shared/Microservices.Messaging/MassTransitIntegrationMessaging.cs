using MassTransit;
using Microservices.Application.Messaging;
using Microservices.Contracts;

namespace Microservices.Messaging;

internal static class IntegrationTransportHeaders
{
    public const string CausationId = "x-causation-id";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        CausationId,
        "MessageId",
        "CorrelationId",
        "InitiatorId",
        "ConversationId",
        "ContentType",
        "MT-MessageType"
    };

    public static void ValidateApplicationHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        if (headers.Count > 32)
        {
            throw new ArgumentException("At most 32 application message headers are supported.", nameof(headers));
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || header.Key.Length > 64)
            {
                throw new ArgumentException("Message header names must contain 1-64 characters.", nameof(headers));
            }

            if (Reserved.Contains(header.Key))
            {
                throw new ArgumentException(
                    $"Message header '{header.Key}' is transport-owned and cannot be supplied by application code.",
                    nameof(headers));
            }

            if (header.Value.Length > 1024)
            {
                throw new ArgumentException(
                    $"Message header '{header.Key}' exceeds the 1024-character limit.",
                    nameof(headers));
            }
        }
    }
}

internal static class IntegrationMessageMetadataApplier
{
    public static void Apply<TMessage>(
        SendContext<TMessage> context,
        IntegrationMessageMetadata? metadata)
        where TMessage : class
    {
        if (metadata?.MessageId is { } messageId)
        {
            context.MessageId = messageId;
        }

        if (metadata?.CorrelationId is { } correlationId)
        {
            context.CorrelationId = correlationId;
        }

        if (metadata?.CausationId is { } causationId)
        {
            context.Headers.Set(IntegrationTransportHeaders.CausationId, causationId);
        }

        if (metadata?.Headers is null)
        {
            return;
        }

        foreach (var header in metadata.Headers)
        {
            context.Headers.Set(header.Key, header.Value);
        }
    }
}

/// <summary>
/// Thin event publishing boundary. MassTransit remains responsible for normal consume-context
/// propagation, conversation identity, correlation conventions, and outbox participation.
/// </summary>
internal sealed class MassTransitIntegrationEventPublisher(
    IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(
        TEvent message,
        IntegrationMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(message);
        IntegrationTransportHeaders.ValidateApplicationHeaders(metadata?.Headers);

        return publishEndpoint.Publish(
            message,
            context => IntegrationMessageMetadataApplier.Apply(context, metadata),
            cancellationToken);
    }
}

/// <summary>
/// Thin command sending boundary. The destination is registered once in infrastructure composition;
/// application handlers send a typed command without knowing a queue or exchange name.
/// </summary>
internal sealed class MassTransitIntegrationCommandSender<TCommand>(
    ISendEndpointProvider sendEndpointProvider,
    Uri destinationAddress) : IIntegrationCommandSender<TCommand>
    where TCommand : class, IIntegrationCommand
{
    public async Task SendAsync(
        TCommand command,
        IntegrationMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        IntegrationTransportHeaders.ValidateApplicationHeaders(metadata?.Headers);

        var endpoint = await sendEndpointProvider
            .GetSendEndpoint(destinationAddress)
            .ConfigureAwait(false);
        await endpoint.Send(
                command,
                context => IntegrationMessageMetadataApplier.Apply(context, metadata),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
