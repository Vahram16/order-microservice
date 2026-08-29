namespace Order.Api.Domain;

public static class OrderErrors
{
    public static readonly OperationError EmptyOrder = OperationError.InvalidInput(
        "order.empty",
        "An order must contain at least one item.");

    public static readonly OperationError InvalidItem = OperationError.InvalidInput(
        "order.invalid_item",
        "An order item contains an invalid product, quantity, price, or product snapshot.");

    public static readonly OperationError DuplicateProduct = OperationError.InvalidInput(
        "order.duplicate_product",
        "An order cannot contain the same product more than once.");

    public static readonly OperationError MixedCurrencies = OperationError.InvalidInput(
        "order.mixed_currencies",
        "All order items must use the same currency.");

    public static readonly OperationError InvalidShippingAddress = OperationError.InvalidInput(
        "order.invalid_shipping_address",
        "The shipping address is incomplete or invalid.");

    public static readonly OperationError InvalidDeadline = OperationError.InvalidInput(
        "order.invalid_deadline",
        "The order checkout deadline must be later than creation time.");

    public static readonly OperationError InvalidState = OperationError.StateConflict(
        "order.invalid_state",
        "The requested order transition is not valid in the current state.");

    public static readonly OperationError WorkflowIdentityConflict = OperationError.StateConflict(
        "order.workflow_identity_conflict",
        "The workflow identifier conflicts with state already recorded for this order.");

    public static readonly OperationError PaymentAmountMismatch = OperationError.StateConflict(
        "order.payment_amount_mismatch",
        "The authorized payment does not match the order total and currency.");

    public static readonly OperationError NotExpired = OperationError.StateConflict(
        "order.not_expired",
        "The order checkout deadline has not elapsed.");
}
