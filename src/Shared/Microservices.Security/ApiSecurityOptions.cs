namespace Microservices.Security;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "Security";

    public string Authority { get; set; } = string.Empty;

    public string? MetadataAddress { get; set; }

    public string Audience { get; set; } = string.Empty;

    public string? RoleClientId { get; set; }

    public bool MapRealmRoles { get; set; }

    public string NameClaimType { get; set; } = "preferred_username";

    public bool RequireHttpsMetadata { get; set; } = true;

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    public string[] ValidTokenTypes { get; set; } = ["JWT", "at+jwt"];
}
