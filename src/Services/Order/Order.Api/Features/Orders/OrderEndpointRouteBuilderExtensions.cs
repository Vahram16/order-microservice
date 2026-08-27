using Order.Api.Features.Orders.Creating.V1;
using Order.Api.Features.Orders.GettingById.V1;

namespace Order.Api.Features.Orders;

internal static class OrderEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/orders").WithTags("Orders");
        CreateOrderEndpoint.Map(group);
        GetOrderByIdEndpoint.Map(group);
        return endpoints;
    }
}
