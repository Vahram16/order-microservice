using Microservices.Contracts;

namespace Microservices.Contracts.Inventory.V1;

public sealed record ReserveInventory(
    Guid OrderId,
    IReadOnlyList<InventoryReservationItem> Items,
    DateTimeOffset ExpiresAtUtc) : IIntegrationCommand
{
    public const string EndpointName = "inventory-reserve-order";
}
