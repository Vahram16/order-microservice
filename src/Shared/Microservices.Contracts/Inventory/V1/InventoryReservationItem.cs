namespace Microservices.Contracts.Inventory.V1;

public sealed record InventoryReservationItem(Guid ProductId, int Quantity);
