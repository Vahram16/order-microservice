using Payment.Api.Domain;

namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentErrorCatalog
{
    private static readonly Dictionary<string, PaymentProblemDescriptor> Descriptors =
        new(StringComparer.Ordinal)
        {
            ["payment.authentication_required"] = new(
                "payment.authentication_required",
                "Authentication required",
                ErrorCategory.AuthenticationRequired,
                "A valid access token is required.",
                false),
            ["payment.invalid_identity_claims"] = new(
                "payment.invalid_identity_claims",
                "Invalid payment identity",
                ErrorCategory.AuthenticationRequired,
                "The access token cannot establish a valid payment identity.",
                false),
            ["payment.invalid_idempotency_key"] = new(
                "payment.invalid_idempotency_key",
                "Invalid idempotency key",
                ErrorCategory.InvalidInput,
                "The idempotency key is malformed.",
                false),
            ["payment.customer_not_synchronized"] = new(
                "payment.customer_not_synchronized",
                "Customer not synchronized",
                ErrorCategory.StateConflict,
                "Payment has not received the authoritative customer identity yet.",
                true),
            ["payment.idempotency_key_reused"] = new(
                "payment.idempotency_key_reused",
                "Idempotency key conflict",
                ErrorCategory.StateConflict,
                "The idempotency key belongs to a different payment customer.",
                false),
            ["payment.method_not_found"] = new(
                "payment.method_not_found",
                "Payment method not found",
                ErrorCategory.MissingResource,
                "The payment method does not exist for this payment customer.",
                false),
            ["payment.provider_unavailable"] = new(
                "payment.provider_unavailable",
                "Payment provider unavailable",
                ErrorCategory.Unexpected,
                "The payment provider did not complete the request.",
                true),
            ["payment.concurrency_conflict"] = new(
                "payment.concurrency_conflict",
                "Payment concurrency conflict",
                ErrorCategory.ConcurrencyConflict,
                "Payment state changed concurrently.",
                true),
            [PaymentErrors.InvalidPaymentCustomerId.Code] = new(
                PaymentErrors.InvalidPaymentCustomerId.Code,
                "Invalid payment customer",
                ErrorCategory.InvalidInput,
                "The payment customer identifier is invalid.",
                false),
            [PaymentErrors.InvalidCustomerId.Code] = new(
                PaymentErrors.InvalidCustomerId.Code,
                "Invalid customer",
                ErrorCategory.InvalidInput,
                "The customer identifier is invalid.",
                false),
            [PaymentErrors.CustomerIdentityConflict.Code] = new(
                PaymentErrors.CustomerIdentityConflict.Code,
                "Customer identity conflict",
                ErrorCategory.StateConflict,
                "The customer identity conflicts with existing payment state.",
                false),
            [PaymentErrors.ProviderCustomerConflict.Code] = new(
                PaymentErrors.ProviderCustomerConflict.Code,
                "Provider customer conflict",
                ErrorCategory.StateConflict,
                "The provider customer link conflicts with existing payment state.",
                false),
            [PaymentErrors.PaymentMethodInactive.Code] = new(
                PaymentErrors.PaymentMethodInactive.Code,
                "Inactive payment method",
                ErrorCategory.StateConflict,
                "An inactive payment method cannot be selected as default.",
                false),
            ["payment.validation"] = new(
                "payment.validation",
                "Payment validation failed",
                ErrorCategory.InvalidInput,
                "A payment-domain value failed validation.",
                false)
        };

    public static PaymentProblemDescriptor GetRequired(OperationError error)
    {
        if (!Descriptors.TryGetValue(error.Code, out var descriptor))
        {
            throw new InvalidOperationException(
                $"Error code '{error.Code}' is not registered in the Payment error catalog.");
        }

        if (descriptor.Category != error.Category)
        {
            throw new InvalidOperationException(
                $"Error category mismatch for '{error.Code}'.");
        }

        return descriptor;
    }

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/errors/v1/payment/{code}",
                IResult (string code) =>
                    Descriptors.TryGetValue(code, out var descriptor)
                        ? Results.Ok(new
                        {
                            type = descriptor.Type,
                            descriptor.Code,
                            descriptor.Title,
                            descriptor.Status,
                            description = descriptor.Description,
                            descriptor.Retryable
                        })
                        : Results.NotFound())
            .WithName("GetPaymentErrorDescriptionV1")
            .WithSummary("Describes a stable version 1 Payment API Problem Details type.")
            .AllowAnonymous();
    }
}
