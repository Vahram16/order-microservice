using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Microservices.Application;

namespace Customer.Api.Features.Customers.UpdatingAddress.V1;

internal sealed record UpdateCustomerAddressCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    Guid AddressId,
    AddressData Address)
    : ICommand<Result<CustomerResponse>>;
