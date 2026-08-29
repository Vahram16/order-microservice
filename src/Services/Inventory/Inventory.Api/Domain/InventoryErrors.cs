namespace Inventory.Api.Domain;

public static class InventoryErrors
{
    public static readonly OperationError InvalidQuantity = OperationError.InvalidInput(
        "inventory.invalid_quantity",
        "Inventory quantities must be non-negative and reservation quantities must be positive.");

    public static readonly OperationError InsufficientStock = OperationError.StateConflict(
        "inventory.insufficient_stock",
        "The requested quantity is not currently available.");

    public static readonly OperationError ReservedStockConflict = OperationError.StateConflict(
        "inventory.reserved_stock_conflict",
        "On-hand stock cannot be set below stock currently reserved by active orders.");

    public static readonly OperationError InvalidReservation = OperationError.InvalidInput(
        "inventory.invalid_reservation",
        "The inventory reservation is incomplete or invalid.");

    public static readonly OperationError InvalidReservationState = OperationError.StateConflict(
        "inventory.invalid_reservation_state",
        "The reservation cannot perform the requested transition in its current state.");

    public static readonly OperationError VersionMismatch = OperationError.ConcurrencyConflict(
        "inventory.version_mismatch",
        "The inventory item changed after the supplied version was issued.");
}
