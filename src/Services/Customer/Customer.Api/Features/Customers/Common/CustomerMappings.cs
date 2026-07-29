using Customer.Api.Domain;

namespace Customer.Api.Features.Customers.Common;

internal static class CustomerMappings
{
    public static CustomerResponse ToResponse(Domain.Customer customer) => new(
        customer.Id,
        customer.FirstName,
        customer.LastName,
        customer.Email,
        customer.PhoneNumber,
        customer.Status.ToString(),
        customer.Addresses
            .OrderByDescending(address => address.IsDefaultShipping)
            .ThenByDescending(address => address.IsDefaultBilling)
            .ThenBy(address => address.CreatedAt)
            .Select(ToResponse)
            .ToArray(),
        customer.CreatedAt,
        customer.UpdatedAt,
        customer.Version);

    private static CustomerAddressResponse ToResponse(CustomerAddress address) => new(
        address.Id,
        address.Label,
        address.RecipientName,
        address.Line1,
        address.Line2,
        address.City,
        address.Region,
        address.PostalCode,
        address.CountryCode.Value,
        address.PhoneNumber,
        address.IsDefaultShipping,
        address.IsDefaultBilling,
        address.CreatedAt,
        address.UpdatedAt);
}
