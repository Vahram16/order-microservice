using Customer.Api.Domain;
using Microservices.Application;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal sealed record AddCustomerAddressCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    Guid AddressId,
    AddressData Address)
    : ICommand<AddCustomerAddressResult>;
