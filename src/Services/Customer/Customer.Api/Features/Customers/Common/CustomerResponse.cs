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
