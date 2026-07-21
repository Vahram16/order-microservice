namespace Identity.Api.Configuration;

public sealed class AuthorizationServerOptions
{
    public const string SectionName = "AuthorizationServer";

    public string? Issuer { get; init; }

    public bool UseEphemeralKeysInDevelopment { get; init; }

    public TimeSpan AuthorizationCodeLifetime { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan IdentityTokenLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(14);

    public List<CertificateOptions> SigningCertificates { get; init; } = [];

    public List<CertificateOptions> EncryptionCertificates { get; init; } = [];

    public List<AuthorizationScopeOptions> Scopes { get; init; } = [];

    public List<string> CorsOrigins { get; init; } = [];

    public List<AuthorizationClientOptions> Clients { get; init; } = [];
}

public sealed class CertificateOptions
{
    public string Path { get; init; } = string.Empty;

    public string? Password { get; init; }
}

public sealed class AuthorizationScopeOptions
{
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Resource { get; init; } = string.Empty;
}

public sealed class AuthorizationClientOptions
{
    public string ClientId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public AuthorizationClientProfile Profile { get; init; }

    public string? ClientSecret { get; init; }

    public string? JsonWebKeySetPath { get; init; }

    public bool AllowRefreshTokens { get; init; }

    public bool RequirePushedAuthorizationRequests { get; init; }

    public List<string> RedirectUris { get; init; } = [];

    public List<string> PostLogoutRedirectUris { get; init; } = [];

    public List<string> Scopes { get; init; } = [];
}

public enum AuthorizationClientProfile
{
    Public,
    Web,
    Service
}
