namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentApplicationErrors
{
    public static OperationError AuthenticationRequired { get; } = OperationError.AuthenticationRequired("payment.authentication_required", "A valid user access token is required.");
    public static OperationError InvalidIdentityClaims { get; } = OperationError.AuthenticationRequired("payment.invalid_identity_claims", "The access token cannot establish a valid payment identity.");
    public static OperationError InvalidIdempotencyKey { get; } = OperationError.InvalidInput("payment.invalid_idempotency_key", "Idempotency-Key must contain exactly one non-empty GUID.");
    public static OperationError CustomerNotSynchronized { get; } = OperationError.StateConflict("payment.customer_not_synchronized", "Payment cannot resolve the authenticated user to an authoritative customer yet.");
    public static OperationError IdempotencyKeyReused { get; } = OperationError.StateConflict("payment.idempotency_key_reused", "The idempotency key was already used by a different payment customer.");
    public static OperationError PaymentMethodNotFound { get; } = OperationError.MissingResource("payment.method_not_found", "The requested payment method was not found.");
    public static OperationError PaymentAttemptNotFound { get; } = OperationError.MissingResource("payment.attempt_not_found", "The requested payment attempt was not found.");
    public static OperationError PaymentActionNotRequired { get; } = OperationError.StateConflict("payment.action_not_required", "The payment attempt does not currently require customer action.");
    public static OperationError ProviderUnavailable { get; } = OperationError.Unexpected("payment.provider_unavailable", "The payment provider could not complete the request. Retry with the same idempotency key.");
    public static OperationError ConcurrencyConflict { get; } = OperationError.ConcurrencyConflict("payment.concurrency_conflict", "Payment state changed concurrently. Reload and retry.");
}
