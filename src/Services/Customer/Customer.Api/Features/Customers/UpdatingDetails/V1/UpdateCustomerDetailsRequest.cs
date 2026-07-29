namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

public sealed record UpdateCustomerDetailsRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber);
