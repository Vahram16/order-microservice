using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record CaptureOrderPayment(Guid OrderId, Guid PaymentAttemptId) : IIntegrationCommand
{
    public const string EndpointName = "payment-capture-order";
}
