using System.Threading;
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

internal sealed class MassTransitIntegrationMessagePublisher(
    IPublishEndpoint publishEndpoint,
    IntegrationConsumeContextAccessor contextAccessor) : IIntegrationMessagePublisher
{
    public Task PublishAsync<TMessage>(
        TMessage message,
        IntegrationPublishMetadata? metadata = null,
        CancellationToken cancellationToken = default)
        where TMessage : class, IIntegrationMessage
    {
        ArgumentNullException.ThrowIfNull(message);
        IntegrationTransportHeaders.ValidateApplicationHeaders(metadata?.Headers);

        var parent = contextAccessor.Current;
        var messageId = metadata?.MessageId ?? NewId.NextGuid();
        var correlationId = metadata?.CorrelationId ?? parent?.CorrelationId ?? messageId;
        var causationId = metadata?.CausationId ?? parent?.MessageId;

        return publishEndpoint.Publish(
            message,
            context =>
            {
                context.MessageId = messageId;
                context.CorrelationId = correlationId;
                context.InitiatorId = causationId;

                if (causationId is not null)
                {
                    context.Headers.Set(IntegrationTransportHeaders.CausationId, causationId.Value);
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

internal sealed class IntegrationConsumeContextAccessor
{
    private readonly AsyncLocal<IntegrationConsumeMetadata?> _current = new();

    public IntegrationConsumeMetadata? Current => _current.Value;

    public IDisposable Push(IntegrationConsumeMetadata metadata)
    {
        var previous = _current.Value;
        _current.Value = metadata;
        return new RestoreScope(this, previous);
    }

    private sealed class RestoreScope(
        IntegrationConsumeContextAccessor owner,
        IntegrationConsumeMetadata? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            owner._current.Value = previous;
            _disposed = true;
        }
    }
}

internal sealed record IntegrationConsumeMetadata(
    Guid? MessageId,
    Guid? CorrelationId);

internal sealed class IntegrationConsumeContextFilter<T>(
    IntegrationConsumeContextAccessor accessor) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        using var scope = accessor.Push(
            new IntegrationConsumeMetadata(context.MessageId, context.CorrelationId));
        await next.Send(context).ConfigureAwait(false);
    }

    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("integrationConsumeContext");
}
