using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record PaymentCaptureFailed(Guid OrderId, Guid PaymentAttemptId, string ReasonCode, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
