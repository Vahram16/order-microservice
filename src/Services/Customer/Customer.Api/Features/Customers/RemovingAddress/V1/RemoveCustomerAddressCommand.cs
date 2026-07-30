using Customer.Api.Features.Customers.Common;
using Microservices.Application;

namespace Customer.Api.Features.Customers.RemovingAddress.V1;

internal sealed record RemoveCustomerAddressCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    Guid AddressId)
    : ICommand<Result<CustomerResponse>>;
