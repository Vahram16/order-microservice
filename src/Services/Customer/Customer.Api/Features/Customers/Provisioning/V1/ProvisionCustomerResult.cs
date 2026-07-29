using Customer.Api.Features.Customers.Common;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal sealed record ProvisionCustomerResult(
    CustomerResponse Customer,
    bool Created);
