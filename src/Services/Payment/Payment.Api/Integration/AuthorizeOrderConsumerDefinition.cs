using MassTransit;
using Microservices.Contracts.Payments.V1;

namespace Payment.Api.Integration;

internal sealed class AuthorizeOrderConsumerDefinition
    : ConsumerDefinition<AuthorizeOrderConsumer>
{
    public AuthorizeOrderConsumerDefinition()
    {
        EndpointName = AuthorizeOrderPayment.EndpointName;
    }
}
