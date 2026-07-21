using Identity.Api.Features.Profile.GettingCurrentProfile.V1;

namespace Identity.Api.Features.Profile;

internal static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCurrentProfile();
        return endpoints;
    }
}
