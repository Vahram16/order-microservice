using Customer.Api.Domain;

namespace Customer.Api.Features.Customers.Common;

public sealed record CustomerResponse(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    string Status,
    IReadOnlyList<CustomerAddressResponse> Addresses,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

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

public sealed record CustomerExportResponse(
    DateTimeOffset ExportedAt,
    CustomerResponse Customer);

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
