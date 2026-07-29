using Customer.Api.Domain;

namespace Customer.Api.Features.Customers.UpdatingAddress.V1;

public sealed record UpdateCustomerAddressRequest(
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
    bool IsDefaultBilling)
{
    internal AddressData ToAddressData() => new(
        Label,
        RecipientName,
        Line1,
        Line2,
        City,
        Region,
        PostalCode,
        CountryCode,
        PhoneNumber,
        IsDefaultShipping,
        IsDefaultBilling);
}
