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
            throw new ArgumentException("At most 32 application publish headers are supported.", nameof(headers));
        }

        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || header.Key.Length > 64)
            {
                throw new ArgumentException("Publish header names must contain 1-64 characters.", nameof(headers));
            }

            if (Reserved.Contains(header.Key))
            {
                throw new ArgumentException(
                    $"Publish header '{header.Key}' is transport-owned and cannot be supplied by application code.",
                    nameof(headers));
            }

            if (header.Value.Length > 1024)
            {
                throw new ArgumentException(
                    $"Publish header '{header.Key}' exceeds the 1024-character limit.",
                    nameof(headers));
            }
        }
    }
}

/// <summary>
/// Thin application-owned publishing boundary. MassTransit remains responsible for normal consume
/// context propagation, conversation identity, correlation conventions, and outbox participation.
/// </summary>
internal sealed class MassTransitIntegrationMessagePublisher(
    IPublishEndpoint publishEndpoint) : IIntegrationMessagePublisher
{
    public Task PublishAsync<TMessage>(
        TMessage message,
        IntegrationPublishMetadata? metadata = null,
        CancellationToken cancellationToken = default)
        where TMessage : class, IIntegrationMessage
    {
        ArgumentNullException.ThrowIfNull(message);
        IntegrationTransportHeaders.ValidateApplicationHeaders(metadata?.Headers);

        return publishEndpoint.Publish(
            message,
            context =>
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

                if (metadata?.Headers is not null)
                {
                    foreach (var header in metadata.Headers)
                    {
                        context.Headers.Set(header.Key, header.Value);
                    }
                }
            },
            cancellationToken);
    }
}
