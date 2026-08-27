using Microservices.Contracts;

namespace Microservices.Contracts.Orders.V1;

public sealed record OrderConfirmed(
    Guid OrderId,
    Guid CustomerId,
    decimal Total,
    string CurrencyCode,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
