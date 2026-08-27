using Microservices.Contracts;

namespace Microservices.Contracts.Customers.V1;

public sealed record CustomerIdentitySnapshotItem(
    Guid CustomerId,
    string IdentityProvider,
    string IdentitySubject,
    DateTimeOffset UpdatedAtUtc);

public sealed record CustomerIdentitySnapshotPage(
    Guid SnapshotId,
    Guid? AfterCustomerId,
    IReadOnlyList<CustomerIdentitySnapshotItem> Items,
    Guid? NextAfterCustomerId,
    bool IsLastPage,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
