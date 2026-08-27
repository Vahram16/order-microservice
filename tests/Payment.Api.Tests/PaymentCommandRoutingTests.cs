using MassTransit;
using Microservices.Contracts.Payments.V1;
using Payment.Api.Integration;

namespace Payment.Api.Tests;

public sealed class PaymentCommandRoutingTests
{
    [Fact]
    public void OrderPaymentCommandConsumersUseDurableContractEndpointNames()
    {
        var routes = new (IConsumerDefinition Definition, string ExpectedEndpoint)[]
        {
            (new AuthorizeOrderConsumerDefinition(), AuthorizeOrderPayment.EndpointName),
            (new CaptureOrderPaymentConsumerDefinition(), CaptureOrderPayment.EndpointName),
            (new CancelOrderConsumerDefinition(), CancelOrderPayment.EndpointName)
        };

        foreach (var route in routes)
        {
            Assert.Equal(
                route.ExpectedEndpoint,
                route.Definition.GetEndpointName(KebabCaseEndpointNameFormatter.Instance));
        }
    }
}
