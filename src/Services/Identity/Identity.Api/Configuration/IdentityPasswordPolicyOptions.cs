namespace Identity.Api.Configuration;

public sealed class IdentityPasswordPolicyOptions
{
    public const string SectionName = "IdentityPasswordPolicy";

    public string? BlocklistPath { get; init; }

    public int MinimumBlocklistEntries { get; init; } = 10_000;

    public bool RejectUserInputs { get; init; } = true;
}
