using Customer.Api.Features.Customers.Common;
using Microservices.Application;

namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

internal sealed record UpdateCustomerDetailsCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber)
    : ICommand<CustomerResponse>;
