using Payment.Api.Domain;

namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentErrorCatalog
{
    private static readonly Dictionary<string, PaymentProblemDescriptor> Descriptors = new(StringComparer.Ordinal)
    {
        ["payment.authentication_required"] = D("payment.authentication_required", "Authentication required", ErrorCategory.AuthenticationRequired, false),
        ["payment.invalid_identity_claims"] = D("payment.invalid_identity_claims", "Invalid payment identity", ErrorCategory.AuthenticationRequired, false),
        ["payment.invalid_idempotency_key"] = D("payment.invalid_idempotency_key", "Invalid idempotency key", ErrorCategory.InvalidInput, false),
        ["payment.customer_not_synchronized"] = D("payment.customer_not_synchronized", "Customer not synchronized", ErrorCategory.StateConflict, true),
        ["payment.idempotency_key_reused"] = D("payment.idempotency_key_reused", "Idempotency key conflict", ErrorCategory.StateConflict, false),
        ["payment.method_not_found"] = D("payment.method_not_found", "Payment method not found", ErrorCategory.MissingResource, false),
        ["payment.attempt_not_found"] = D("payment.attempt_not_found", "Payment attempt not found", ErrorCategory.MissingResource, false),
        ["payment.action_not_required"] = D("payment.action_not_required", "Payment action not required", ErrorCategory.StateConflict, false),
        ["payment.provider_unavailable"] = D("payment.provider_unavailable", "Payment provider unavailable", ErrorCategory.Unexpected, true),
        ["payment.concurrency_conflict"] = D("payment.concurrency_conflict", "Payment concurrency conflict", ErrorCategory.ConcurrencyConflict, true),
        [PaymentErrors.InvalidPaymentCustomerId.Code] = D(PaymentErrors.InvalidPaymentCustomerId.Code, "Invalid payment customer", ErrorCategory.InvalidInput, false),
        [PaymentErrors.InvalidCustomerId.Code] = D(PaymentErrors.InvalidCustomerId.Code, "Invalid customer", ErrorCategory.InvalidInput, false),
        [PaymentErrors.CustomerIdentityConflict.Code] = D(PaymentErrors.CustomerIdentityConflict.Code, "Customer identity conflict", ErrorCategory.StateConflict, false),
        [PaymentErrors.ProviderCustomerConflict.Code] = D(PaymentErrors.ProviderCustomerConflict.Code, "Provider customer conflict", ErrorCategory.StateConflict, false),
        [PaymentErrors.PaymentMethodInactive.Code] = D(PaymentErrors.PaymentMethodInactive.Code, "Inactive payment method", ErrorCategory.StateConflict, false),
        [PaymentErrors.OrderPaymentConflict.Code] = D(PaymentErrors.OrderPaymentConflict.Code, "Order payment conflict", ErrorCategory.StateConflict, false),
        [PaymentErrors.OrderPaymentInvalidState.Code] = D(PaymentErrors.OrderPaymentInvalidState.Code, "Invalid order payment state", ErrorCategory.StateConflict, false),
        ["payment.validation"] = D("payment.validation", "Payment validation failed", ErrorCategory.InvalidInput, false)
    };

    public static PaymentProblemDescriptor GetRequired(OperationError error)
    {
        if (!Descriptors.TryGetValue(error.Code, out var descriptor)) throw new InvalidOperationException($"Error code '{error.Code}' is not registered in the Payment error catalog.");
        if (descriptor.Category != error.Category) throw new InvalidOperationException($"Error category mismatch for '{error.Code}'.");
        return descriptor;
    }

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/errors/v1/payment/{code}", IResult (string code) =>
            Descriptors.TryGetValue(code, out var descriptor)
                ? Results.Ok(new { type = descriptor.Type, descriptor.Code, descriptor.Title, descriptor.Status, description = descriptor.Description, descriptor.Retryable })
                : Results.NotFound())
            .WithName("GetPaymentErrorDescriptionV1")
            .WithSummary("Describes a stable version 1 Payment API Problem Details type.")
            .AllowAnonymous();

    private static PaymentProblemDescriptor D(string code, string title, ErrorCategory category, bool retryable) => new(code, title, category, title, retryable);
}
