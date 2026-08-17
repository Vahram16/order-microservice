using Microservices.Primitives;

namespace Payment.Api.Domain;

public static class PaymentErrors
{
    public static OperationError InvalidPaymentCustomerId { get; } =
        OperationError.InvalidInput(
            "payment.invalid_payment_customer_id",
            "Payment customer identifier cannot be empty.");

    public static OperationError InvalidCustomerId { get; } =
        OperationError.InvalidInput(
            "payment.invalid_customer_id",
            "Customer identifier cannot be empty.");

    public static OperationError CustomerIdentityConflict { get; } =
        OperationError.StateConflict(
            "payment.customer_identity_conflict",
            "The customer identity is already associated with different payment state.");

    public static OperationError ProviderCustomerConflict { get; } =
        OperationError.StateConflict(
            "payment.provider_customer_conflict",
            "The payment customer is already linked to a different provider customer.");

    public static OperationError PaymentMethodInactive { get; } =
        OperationError.StateConflict(
            "payment.method_inactive",
            "An inactive payment method cannot be selected as default.");

    public static OperationError Validation(string field, string description) =>
        OperationError.InvalidInput(
            "payment.validation",
            description,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field
            });
}
