using Inventory.Api.Features.Inventory.SettingStock.V1;

namespace Inventory.Api.Features.Inventory;

internal static class InventoryEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory").WithTags("Inventory");
        SetInventoryStockEndpoint.Map(group);
        return endpoints;
    }
}
