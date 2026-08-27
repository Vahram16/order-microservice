namespace Payment.Api.Domain;

public enum OrderPaymentStatus
{
    Pending = 1,
    RequiresCustomerAction = 2,
    Authorized = 3,
    Rejected = 4,
    Cancelled = 5
}
