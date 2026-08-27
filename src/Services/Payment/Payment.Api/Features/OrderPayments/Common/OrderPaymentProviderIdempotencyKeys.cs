namespace Payment.Api.Features.OrderPayments.Common;

internal static class OrderPaymentProviderIdempotencyKeys
{
    public static string Create(Guid orderId) => $"order-payment-create:{orderId:N}";
    public static string Confirm(Guid orderId) => $"order-payment-confirm:{orderId:N}";
    public static string Capture(Guid orderId) => $"order-payment-capture:{orderId:N}";
    public static string Cancel(Guid orderId) => $"order-payment-cancel:{orderId:N}";
    public static string Refund(Guid orderId) => $"order-payment-refund:{orderId:N}";
}
