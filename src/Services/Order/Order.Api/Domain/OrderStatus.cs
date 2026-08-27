namespace Order.Api.Domain;

public enum OrderStatus
{
    AwaitingInventory = 1,
    PaymentAuthorizing = 2,
    AwaitingPaymentAction = 3,
    PaymentAuthorized = 4,
    Confirmed = 5,
    Cancelled = 6,
    Expired = 7
}
