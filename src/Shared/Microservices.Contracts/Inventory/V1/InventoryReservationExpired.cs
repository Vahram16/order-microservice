using Microservices.Contracts;

namespace Microservices.Contracts.Inventory.V1;

public sealed record InventoryReservationExpired(
    Guid OrderId,
    Guid ReservationId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
