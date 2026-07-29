namespace Customer.Api.Features.Customers.Common;

public sealed record CustomerAddressResponse(
    Guid Id,
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? PhoneNumber,
    bool IsDefaultShipping,
    bool IsDefaultBilling,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
