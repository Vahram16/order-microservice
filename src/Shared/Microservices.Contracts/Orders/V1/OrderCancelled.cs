using Microservices.Contracts;

namespace Microservices.Contracts.Orders.V1;

public sealed record OrderCancelled(
    Guid OrderId,
    Guid CustomerId,
    string ReasonCode,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
