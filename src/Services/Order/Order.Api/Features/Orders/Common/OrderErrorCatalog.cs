using Order.Api.Domain;

namespace Order.Api.Features.Orders.Common;

internal static class OrderErrorCatalog
{
    private static readonly Dictionary<string, OrderProblemDescriptor> Descriptors = new(StringComparer.Ordinal)
    {
        ["order.authentication_required"] = Descriptor("order.authentication_required", "Authentication required", ErrorCategory.AuthenticationRequired, false),
        ["order.invalid_identity_claims"] = Descriptor("order.invalid_identity_claims", "Invalid identity", ErrorCategory.AuthenticationRequired, false),
        ["order.invalid_idempotency_key"] = Descriptor("order.invalid_idempotency_key", "Invalid idempotency key", ErrorCategory.InvalidInput, false),
        ["order.idempotency_key_reused"] = Descriptor("order.idempotency_key_reused", "Idempotency conflict", ErrorCategory.StateConflict, false),
        ["order.customer_not_synchronized"] = Descriptor("order.customer_not_synchronized", "Customer synchronization pending", ErrorCategory.StateConflict, true),
        ["order.catalog_not_synchronized"] = Descriptor("order.catalog_not_synchronized", "Catalog synchronization pending", ErrorCategory.StateConflict, true),
        ["order.product_unavailable"] = Descriptor("order.product_unavailable", "Product unavailable", ErrorCategory.StateConflict, false),
        ["order.not_found"] = Descriptor("order.not_found", "Order not found", ErrorCategory.MissingResource, false),
        ["order.concurrency_conflict"] = Descriptor("order.concurrency_conflict", "Order concurrency conflict", ErrorCategory.ConcurrencyConflict, true),
        ["order.empty"] = Descriptor("order.empty", "Empty order", ErrorCategory.InvalidInput, false),
        ["order.invalid_item"] = Descriptor("order.invalid_item", "Invalid order item", ErrorCategory.InvalidInput, false),
        ["order.duplicate_product"] = Descriptor("order.duplicate_product", "Duplicate product", ErrorCategory.InvalidInput, false),
        ["order.mixed_currencies"] = Descriptor("order.mixed_currencies", "Mixed currencies", ErrorCategory.InvalidInput, false),
        ["order.invalid_shipping_address"] = Descriptor("order.invalid_shipping_address", "Invalid shipping address", ErrorCategory.InvalidInput, false),
        ["order.invalid_deadline"] = Descriptor("order.invalid_deadline", "Invalid checkout deadline", ErrorCategory.InvalidInput, false),
        ["order.invalid_state"] = Descriptor("order.invalid_state", "Invalid order state", ErrorCategory.StateConflict, false),
        ["order.workflow_identity_conflict"] = Descriptor("order.workflow_identity_conflict", "Order workflow conflict", ErrorCategory.StateConflict, false),
        ["order.payment_amount_mismatch"] = Descriptor("order.payment_amount_mismatch", "Payment amount mismatch", ErrorCategory.StateConflict, false),
        ["order.not_expired"] = Descriptor("order.not_expired", "Order not expired", ErrorCategory.StateConflict, false)
    };

    internal static OrderProblemDescriptor GetRequired(OperationError error)
    {
        if (!Descriptors.TryGetValue(error.Code, out var descriptor) || descriptor.Category != error.Category)
        {
            throw new InvalidOperationException($"Error code '{error.Code}' is not registered consistently in the Order error catalog.");
        }

        return descriptor;
    }

    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/errors/v1/order/{code}", IResult (string code) =>
            Descriptors.TryGetValue(code, out var descriptor)
                ? Results.Ok(new { type = descriptor.Type, descriptor.Code, descriptor.Title, descriptor.Status, description = descriptor.Description, descriptor.Retryable })
                : Results.NotFound())
            .WithName("GetOrderErrorDescriptionV1")
            .AllowAnonymous();

    private static OrderProblemDescriptor Descriptor(string code, string title, ErrorCategory category, bool retryable) =>
        new(code, title, category, title, retryable);
}
