using Microservices.Contracts;

namespace Microservices.Contracts.Inventory.V1;

public sealed record ReleaseInventory(Guid OrderId, Guid ReservationId, string Reason) : IIntegrationCommand
{
    public const string EndpointName = "inventory-release-order";
}
