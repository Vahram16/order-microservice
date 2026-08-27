using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record PaymentCaptured(Guid OrderId, Guid PaymentAttemptId, decimal Amount, string CurrencyCode, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
