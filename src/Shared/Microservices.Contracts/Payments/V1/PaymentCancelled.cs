using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record PaymentCancelled(
    Guid OrderId,
    Guid PaymentAttemptId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
