namespace Microservices.Security;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "Security";

    public string Authority { get; set; } = string.Empty;

    public string? MetadataAddress { get; set; }

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

    public string[] ValidTokenTypes { get; set; } = ["at+jwt"];
}
