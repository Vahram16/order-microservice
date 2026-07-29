using Customer.Api.Domain;

namespace Customer.Api.Features.Customers.Common;

internal static class CustomerApplicationErrors
{
    public static OperationError CustomerNotFound { get; } = OperationError.MissingResource(
        "customer.not_found",
        "Customer was not found.");

    public static OperationError PreconditionRequired { get; } = OperationError.PreconditionRequired(
        "customer.precondition_required",
        "An If-Match header containing the current customer ETag is required.");

    public static OperationError InvalidPrecondition { get; } = OperationError.InvalidInput(
        "customer.invalid_precondition",
        "If-Match must contain exactly one strong customer ETag.");

    public static OperationError InvalidIdempotencyKey { get; } = OperationError.InvalidInput(
        "customer.invalid_idempotency_key",
        "Idempotency-Key must contain exactly one non-empty GUID.");

    public static OperationError AuthenticationRequired { get; } = OperationError.AuthenticationRequired(
        "customer.authentication_required",
        "A valid user access token is required.");

    public static OperationError InvalidIdentityClaims { get; } = OperationError.AuthenticationRequired(
        "customer.invalid_identity_claims",
        "The access token contains identity claims that cannot establish a valid customer identity.");

    public static OperationError DefaultShippingConflict { get; } = OperationError.ConcurrencyConflict(
        "customer.default_shipping_conflict",
        "Another request changed the default shipping address. Reload the customer and retry.");

    public static OperationError DefaultBillingConflict { get; } = OperationError.ConcurrencyConflict(
        "customer.default_billing_conflict",
        "Another request changed the default billing address. Reload the customer and retry.");

    public static OperationError IdempotencyKeyReused { get; } = OperationError.StateConflict(
        "customer.idempotency_key_reused",
        "The idempotency key was already used with different request data.");

    public static OperationError TranslateDomain(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return string.Equals(
            error.Code,
            CustomerErrors.AddressIdentityConflict.Code,
            StringComparison.Ordinal)
            ? IdempotencyKeyReused
            : error;
    }
}
