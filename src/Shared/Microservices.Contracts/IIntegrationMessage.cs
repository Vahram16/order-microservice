namespace Microservices.Contracts;

/// <summary>
/// Required envelope metadata for application-owned integration messages.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MessageId"/> identifies one logical message and must remain stable across safe replay.
/// <see cref="CorrelationId"/> identifies the business operation. <see cref="CausationId"/> identifies
/// the consumed parent message when this message is produced by a consumer.
/// </para>
/// <para>
/// <see cref="ContractVersion"/> starts at one and changes only for a deliberately introduced
/// contract version. Additive changes to an existing contract do not increment this value.
/// </para>
/// </remarks>
public interface IIntegrationMessage
{
    Guid MessageId { get; }

    Guid CorrelationId { get; }

    Guid? CausationId { get; }

    int ContractVersion { get; }
}
