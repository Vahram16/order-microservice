using Customer.Api.Features.Customers.Common;
using Microservices.Application;

namespace Customer.Api.Features.Customers.GettingCurrent.V1;

internal sealed record GetCurrentCustomerQuery(
    string Provider,
    string Subject)
    : IQuery<CustomerResponse?>;
