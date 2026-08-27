using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record CancelOrderPayment(Guid OrderId, Guid PaymentAttemptId, string Reason) : IIntegrationCommand
{
    public const string EndpointName = "payment-cancel-order";
}
