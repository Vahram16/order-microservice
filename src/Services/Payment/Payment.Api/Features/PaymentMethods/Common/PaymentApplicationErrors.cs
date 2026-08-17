namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentApplicationErrors
{
    public static OperationError AuthenticationRequired { get; } = OperationError.AuthenticationRequired(
        "payment.authentication_required",
        "A valid user access token is required.");

    public static OperationError InvalidIdempotencyKey { get; } = OperationError.InvalidInput(
        "payment.invalid_idempotency_key",
        "Idempotency-Key must contain exactly one non-empty GUID.");

    public static OperationError CustomerNotSynchronized { get; } = OperationError.StateConflict(
        "payment.customer_not_synchronized",
        "The payment profile is not synchronized with the customer account yet.");
}
