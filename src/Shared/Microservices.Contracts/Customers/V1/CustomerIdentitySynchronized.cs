namespace Microservices.Contracts.Customers.V1;

public sealed record CustomerIdentitySynchronized(
    Guid CustomerId,
    string IdentityProvider,
    string IdentitySubject,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
