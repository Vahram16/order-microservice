using MassTransit;
using Microservices.Contracts;

namespace Microservices.Messaging;

internal static class IntegrationMessageHeaders
{
    public const string CausationId = "x-causation-id";
    public const string ContractVersion = "x-contract-version";
}

internal sealed class IntegrationMessageIdentityException(string message)
    : Exception(message), IPermanentConsumerFailure;

internal sealed class IntegrationMessageSendFilter<T> : IFilter<SendContext<T>>
    where T : class
{
    public Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        if (context.Message is IIntegrationMessage message)
        {
            IntegrationMessageIdentity.Validate(message);
            context.MessageId = message.MessageId;
            context.CorrelationId = message.CorrelationId;
            context.Headers.Set(IntegrationMessageHeaders.ContractVersion, message.ContractVersion);

            if (message.CausationId is not null)
            {
                context.Headers.Set(IntegrationMessageHeaders.CausationId, message.CausationId.Value);
            }
        }

        return next.Send(context);
    }

    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("integrationMessageIdentity");
}

internal sealed class IntegrationMessageConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Message is IIntegrationMessage message)
        {
            IntegrationMessageIdentity.Validate(message);

            if (context.MessageId != message.MessageId)
            {
                throw new IntegrationMessageIdentityException(
                    "The transport MessageId does not match the integration message MessageId.");
            }

            if (context.CorrelationId != message.CorrelationId)
            {
                throw new IntegrationMessageIdentityException(
                    "The transport CorrelationId does not match the integration message CorrelationId.");
            }

            var contractVersion = context.Headers.Get(
                IntegrationMessageHeaders.ContractVersion,
                default(int?));
            if (contractVersion != message.ContractVersion)
            {
                throw new IntegrationMessageIdentityException(
                    "The transport contract version does not match the integration message version.");
            }

            var causationId = context.Headers.Get(
                IntegrationMessageHeaders.CausationId,
                default(Guid?));
            if (causationId != message.CausationId)
            {
                throw new IntegrationMessageIdentityException(
                    "The transport causation identifier does not match the integration message causation identifier.");
            }
        }

        return next.Send(context);
    }

    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("integrationMessageIdentity");
}

internal static class IntegrationMessageIdentity
{
    public static void Validate(IIntegrationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.MessageId == Guid.Empty)
        {
            throw new IntegrationMessageIdentityException("Integration message MessageId is required.");
        }

        if (message.CorrelationId == Guid.Empty)
        {
            throw new IntegrationMessageIdentityException("Integration message CorrelationId is required.");
        }

        if (message.CausationId == Guid.Empty)
        {
            throw new IntegrationMessageIdentityException(
                "Integration message CausationId cannot be an empty identifier.");
        }

        if (message.ContractVersion <= 0)
        {
            throw new IntegrationMessageIdentityException(
                "Integration message ContractVersion must be greater than zero.");
        }
    }
}
