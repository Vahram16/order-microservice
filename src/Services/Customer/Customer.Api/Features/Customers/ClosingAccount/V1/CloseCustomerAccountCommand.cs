using Customer.Api.Features.Customers.Common;
using Microservices.Application;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal sealed record CloseCustomerAccountCommand(
    string Provider,
    string Subject,
    long ExpectedVersion)
    : ICommand<Result<CustomerResponse>>;
