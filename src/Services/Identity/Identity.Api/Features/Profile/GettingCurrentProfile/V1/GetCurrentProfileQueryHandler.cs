using Identity.Api.Model;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Features.Profile.GettingCurrentProfile.V1;

internal sealed class GetCurrentProfileQueryHandler(
    UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetCurrentProfileQuery, CurrentProfileResult?>
{
    public async Task<CurrentProfileResult?> Handle(
        GetCurrentProfileQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString("D"));
        return user is { IsActive: true, Email: not null }
            ? new CurrentProfileResult(
                user.Id,
                user.DisplayName,
                user.Email,
                user.EmailConfirmed)
            : null;
    }
}
