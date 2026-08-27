namespace Order.Api.Features.Orders.Common;

internal sealed record OrderItemResponse(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
