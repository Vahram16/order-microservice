namespace Order.Api.Features.Orders.Common;

internal sealed record OrderShippingAddressResponse(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? PhoneNumber);
