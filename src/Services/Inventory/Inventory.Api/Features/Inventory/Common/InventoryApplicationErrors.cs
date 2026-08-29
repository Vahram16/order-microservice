namespace Inventory.Api.Features.Inventory.Common;

internal static class InventoryApplicationErrors
{
    public static readonly OperationError NotFound = OperationError.MissingResource("inventory.not_found", "Inventory was not found for the requested product.");
    public static readonly OperationError AlreadyExists = OperationError.StateConflict("inventory.already_exists", "Inventory already exists for the requested product; supply its current ETag to update it.");
    public static readonly OperationError PreconditionRequired = OperationError.PreconditionRequired("inventory.precondition_required", "A current strong inventory ETag is required to update existing stock.");
    public static readonly OperationError InvalidPrecondition = OperationError.InvalidInput("inventory.invalid_precondition", "The If-Match header is not a valid strong inventory ETag.");
}
