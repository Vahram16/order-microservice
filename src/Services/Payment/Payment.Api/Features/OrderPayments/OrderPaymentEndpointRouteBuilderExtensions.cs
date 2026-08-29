using Payment.Api.Features.OrderPayments.GettingAction.V1;

namespace Payment.Api.Features.OrderPayments;

internal static class OrderPaymentEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapOrderPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payment-attempts").WithTags("Order Payments");
        GetOrderPaymentActionEndpoint.Map(group);
        return endpoints;
    }
}
