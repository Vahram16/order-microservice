using Microservices.Contracts;

namespace Microservices.Contracts.Inventory.V1;

public sealed record InventoryReserved(
    Guid OrderId,
    Guid ReservationId,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
