namespace Order.Api.Features.Orders.Creating.V1;

internal sealed record CreateOrderRequest(
    IReadOnlyList<CreateOrderItemRequest> Items,
    Guid PaymentMethodId,
    CreateOrderShippingAddressRequest ShippingAddress);
