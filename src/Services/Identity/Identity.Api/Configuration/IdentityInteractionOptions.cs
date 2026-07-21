namespace Identity.Api.Configuration;

public sealed class IdentityInteractionOptions
{
    public const string SectionName = "IdentityInteraction";

    public string? PublicOrigin { get; init; }

    public string LoginPath { get; init; } = "/account/login";

    public string LogoutPath { get; init; } = "/account/logout";

    public string AccessDeniedPath { get; init; } = "/account/access-denied";

    public TimeSpan InteractionTokenLifetime { get; init; } = TimeSpan.FromMinutes(3);
}
