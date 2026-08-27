namespace Payment.Api.Domain;

public enum OrderPaymentStatus
{
    Pending = 1,
    RequiresCustomerAction = 2,
    Authorized = 3,
    Rejected = 4,
    Cancelled = 5,
    Captured = 6,
    CaptureFailed = 7,
    CancellationRequested = 8,
    RefundPending = 9,
    Refunded = 10,
    RefundFailed = 11
}
