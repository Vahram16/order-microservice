using Microservices.Contracts;

namespace Microservices.Contracts.Customers.V1;

public sealed record SynchronizeCustomerIdentitySnapshot(Guid SnapshotId, Guid? AfterCustomerId, int PageSize) : IIntegrationCommand
{
    public const string EndpointName = "customer-synchronize-identity-snapshot";
}
