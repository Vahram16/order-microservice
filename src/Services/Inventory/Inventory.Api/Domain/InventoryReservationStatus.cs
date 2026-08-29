namespace Inventory.Api.Domain;

public enum InventoryReservationStatus
{
    Active = 1,
    Rejected = 2,
    Released = 3,
    Committed = 4,
    Expired = 5
}
