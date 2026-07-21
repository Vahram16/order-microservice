namespace Identity.Api.Features.Profile.GettingCurrentProfile.V1;

internal sealed record CurrentProfileResult(
    Guid Id,
    string DisplayName,
    string Email,
    bool EmailConfirmed);
