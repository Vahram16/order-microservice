namespace Order.Api.Features.Orders.Creating.V1;

internal sealed record CreateOrderShippingAddressRequest(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? PhoneNumber);
