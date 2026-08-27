using Microservices.Contracts;

namespace Microservices.Contracts.Products.V1;

public sealed record ProductCatalogSnapshotCompleted(Guid SnapshotId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
