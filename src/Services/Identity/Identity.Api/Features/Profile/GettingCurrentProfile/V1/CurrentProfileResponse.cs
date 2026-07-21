namespace Identity.Api.Features.Profile.GettingCurrentProfile.V1;

public sealed record CurrentProfileResponse(
    Guid Id,
    string DisplayName,
    string Email,
    bool EmailConfirmed);
