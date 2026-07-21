namespace Microservices.Contracts;

public interface IIntegrationEvent
{
    Guid MessageId { get; }

    DateTimeOffset OccurredAtUtc { get; }
}
