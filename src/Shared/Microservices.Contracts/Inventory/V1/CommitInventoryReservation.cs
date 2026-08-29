using Microservices.Contracts;

namespace Microservices.Contracts.Inventory.V1;

public sealed record CommitInventoryReservation(Guid OrderId, Guid ReservationId) : IIntegrationCommand
{
    public const string EndpointName = "inventory-commit-order";
}
