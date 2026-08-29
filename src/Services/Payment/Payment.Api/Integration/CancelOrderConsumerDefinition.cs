using MassTransit;
using Microservices.Contracts.Payments.V1;

namespace Payment.Api.Integration;

internal sealed class CancelOrderConsumerDefinition
    : ConsumerDefinition<CancelOrderConsumer>
{
    public CancelOrderConsumerDefinition()
    {
        EndpointName = CancelOrderPayment.EndpointName;
    }
}
