namespace Order.Api.Features.Orders.Common;

internal static class OrderMappings
{
    public static OrderResponse ToResponse(Domain.Order order) =>
        new(
            order.Id,
            order.Status.ToString(),
            order.Total,
            order.CurrencyCode,
            order.PaymentAttemptId,
            order.ExpiresAt,
            order.CreatedAt,
            order.UpdatedAt,
            order.Version,
            order.Items.Select(item => new OrderItemResponse(
                item.ProductId,
                item.Sku,
                item.ProductName,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal)).ToArray(),
            new OrderShippingAddressResponse(
                order.ShippingAddress.RecipientName,
                order.ShippingAddress.Line1,
                order.ShippingAddress.Line2,
                order.ShippingAddress.City,
                order.ShippingAddress.Region,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.CountryCode,
                order.ShippingAddress.PhoneNumber),
            order.TerminalReasonCode);
}
