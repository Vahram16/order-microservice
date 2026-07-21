using Microservices.Application;

namespace Identity.Api.Features.Profile.GettingCurrentProfile.V1;

internal sealed record GetCurrentProfileQuery(Guid UserId)
    : IQuery<CurrentProfileResult?>;
