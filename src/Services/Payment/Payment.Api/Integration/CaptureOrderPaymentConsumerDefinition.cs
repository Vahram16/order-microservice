using MassTransit;
using Microservices.Contracts.Payments.V1;

namespace Payment.Api.Integration;

internal sealed class CaptureOrderPaymentConsumerDefinition
    : ConsumerDefinition<CaptureOrderPaymentConsumer>
{
    public CaptureOrderPaymentConsumerDefinition()
    {
        EndpointName = CaptureOrderPayment.EndpointName;
    }
}
