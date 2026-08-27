namespace Order.Api.Features.Orders.Common;

internal sealed record OrderResponse(
    Guid Id,
    string Status,
    decimal Total,
    string CurrencyCode,
    Guid? PaymentAttemptId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version,
    IReadOnlyList<OrderItemResponse> Items,
    OrderShippingAddressResponse ShippingAddress,
    string? ReasonCode);
