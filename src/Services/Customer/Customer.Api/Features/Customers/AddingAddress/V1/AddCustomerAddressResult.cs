using Customer.Api.Features.Customers.Common;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal sealed record AddCustomerAddressResult(
    CustomerResponse Customer,
    Guid AddressId,
    bool Created);
