using Microservices.Contracts;

namespace Microservices.Contracts.Products.V1;

public sealed record ProductCatalogSnapshotItem(
    Guid ProductId,
    string Sku,
    string Name,
    decimal Price,
    string CurrencyCode,
    long Version,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProductCatalogSnapshotPage(
    Guid SnapshotId,
    Guid? AfterProductId,
    IReadOnlyList<ProductCatalogSnapshotItem> Items,
    Guid? NextAfterProductId,
    bool IsLastPage,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
