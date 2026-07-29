using Customer.Api.Infrastructure;
using Microservices.Application;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal sealed record ProvisionCustomerCommand(CurrentIdentity Identity)
    : ICommand<ProvisionCustomerResult>;
