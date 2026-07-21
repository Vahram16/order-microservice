using System.Security.Claims;
using Identity.Api.Infrastructure;
using MediatR;
using OpenIddict.Abstractions;

namespace Identity.Api.Features.Profile.GettingCurrentProfile.V1;

internal static class GetCurrentProfileEndpoint
{
    public static IEndpointRouteBuilder MapCurrentProfile(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/profile/me",
                async (
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var subject = principal.GetClaim(OpenIddictConstants.Claims.Subject);
                    if (!Guid.TryParse(subject, out var userId))
                    {
                        return Results.Unauthorized();
                    }

                    var profile = await sender.Send(
                        new GetCurrentProfileQuery(userId),
                        cancellationToken);
                    if (profile is null)
                    {
                        return Results.Unauthorized();
                    }

                    return Results.Ok(
                        new CurrentProfileResponse(
                            profile.Id,
                            profile.DisplayName,
                            profile.Email,
                            profile.EmailConfirmed));
                })
            .RequireAuthorization(IdentityServiceExtensions.ProfilePolicy)
            .RequireCors(IdentityServiceExtensions.BrowserCorsPolicy)
            .Produces<CurrentProfileResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetCurrentIdentityProfile")
            .WithSummary("Get the profile bound to the current access-token subject.");

        return endpoints;
    }
}
