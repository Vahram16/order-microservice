using Product.Api.Features.Products.Creating.V1;
using Product.Api.Features.Products.Deleting.V1;
using Product.Api.Features.Products.GettingById.V1;
using Product.Api.Features.Products.Listing.V1;
using Product.Api.Features.Products.Updating.V1;

namespace Product.Api.Features.Products;

internal static class ProductEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/products")
            .WithTags("Products");

        CreateProductEndpoint.Map(group);
        GetProductByIdEndpoint.Map(group);
        ListProductsEndpoint.Map(group);
        UpdateProductEndpoint.Map(group);
        DeleteProductEndpoint.Map(group);

        return endpoints;
    }
}
