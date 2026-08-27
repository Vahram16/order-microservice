using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record PaymentActionRequired(
    Guid OrderId,
    Guid PaymentAttemptId,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
