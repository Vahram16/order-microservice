using System.Security.Claims;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using MediatR;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Identity.Api.Features.Profile.GettingCurrentProfile.V1;

public sealed record GetCurrentProfile(string Subject)
    : IQuery<CurrentProfile?>;

public sealed record CurrentProfile(
    Guid Id,
    string DisplayName,
    string Email,
    bool EmailConfirmed);

internal sealed class GetCurrentProfileHandler(
    UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetCurrentProfile, CurrentProfile?>
{
    public async Task<CurrentProfile?> Handle(
        GetCurrentProfile query,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(query.Subject, out var userId))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        return user is { IsActive: true, Email: not null }
            ? new CurrentProfile(
                user.Id,
                user.DisplayName,
                user.Email,
                user.EmailConfirmed)
            : null;
    }
}

internal static class GetCurrentProfileEndpoint
{
    public static IEndpointRouteBuilder MapCurrentProfile(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/profile/me",
                async (ClaimsPrincipal principal, ISender sender, CancellationToken cancellationToken) =>
                {
                    var subject = principal.GetClaim(OpenIddictConstants.Claims.Subject);
                    if (string.IsNullOrWhiteSpace(subject))
                    {
                        return Results.Unauthorized();
                    }

                    var profile = await sender.Send(
                        new GetCurrentProfile(subject),
                        cancellationToken);
                    return profile is null
                        ? Results.Unauthorized()
                        : Results.Ok(profile);
                })
            .RequireAuthorization(IdentityServiceExtensions.ProfilePolicy)
            .Produces<CurrentProfile>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithName("GetCurrentIdentityProfile")
            .WithSummary("Get the profile bound to the current access-token subject.");

        return endpoints;
    }
}
