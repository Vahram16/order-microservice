using Microservices.Contracts;

namespace Microservices.Contracts.Orders.V1;

public sealed record OrderExpired(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
