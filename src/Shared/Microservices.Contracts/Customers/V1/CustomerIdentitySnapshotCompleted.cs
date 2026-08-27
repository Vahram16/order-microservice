using Microservices.Contracts;

namespace Microservices.Contracts.Customers.V1;

public sealed record CustomerIdentitySnapshotCompleted(Guid SnapshotId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
