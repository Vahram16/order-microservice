namespace Order.Api.Features.Orders.Common;

internal static class OrderApplicationErrors
{
    public static readonly OperationError AuthenticationRequired = OperationError.AuthenticationRequired("order.authentication_required", "Authentication is required.");
    public static readonly OperationError InvalidIdentityClaims = OperationError.AuthenticationRequired("order.invalid_identity_claims", "The authenticated identity is incomplete or invalid.");
    public static readonly OperationError InvalidIdempotencyKey = OperationError.InvalidInput("order.invalid_idempotency_key", "A non-empty GUID Idempotency-Key header is required.");
    public static readonly OperationError IdempotencyKeyReused = OperationError.StateConflict("order.idempotency_key_reused", "The Idempotency-Key was already used for a different order request.");
    public static readonly OperationError ReferenceDataSynchronizing = OperationError.StateConflict("order.reference_data_synchronizing", "Order reference data is still synchronizing. Retry shortly.");
    public static readonly OperationError CustomerNotSynchronized = OperationError.StateConflict("order.customer_not_synchronized", "The authenticated customer is not synchronized to Order yet.");
    public static readonly OperationError CatalogNotSynchronized = OperationError.StateConflict("order.catalog_not_synchronized", "One or more requested products are not synchronized to Order yet.");
    public static readonly OperationError ProductUnavailable = OperationError.StateConflict("order.product_unavailable", "One or more requested products are unavailable.");
    public static readonly OperationError OrderNotFound = OperationError.MissingResource("order.not_found", "The requested order was not found.");
    public static readonly OperationError ConcurrencyConflict = OperationError.ConcurrencyConflict("order.concurrency_conflict", "The order changed concurrently. Retry using current state.");
}
