namespace Order.Api.Features.Orders.Creating.V1;

internal sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);
