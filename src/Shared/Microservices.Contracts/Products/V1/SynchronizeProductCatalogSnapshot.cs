using Microservices.Contracts;

namespace Microservices.Contracts.Products.V1;

public sealed record SynchronizeProductCatalogSnapshot(Guid SnapshotId, Guid? AfterProductId, int PageSize) : IIntegrationCommand
{
    public const string EndpointName = "product-synchronize-catalog-snapshot";
}
