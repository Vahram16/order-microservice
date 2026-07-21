using System.Security.Claims;
using Microservices.Security;

namespace Microservices.Security.Tests;

public sealed class SecurityClaimsPrincipalExtensionsTests
{
    [Fact]
    public void HelpersReturnStandardSecurityClaims()
    {
        var principal = Principal(
            new Claim("sub", "customer-123"),
            new Claim("client_id", "booking-web"),
            new Claim("tenant_id", "tenant-456"));

        Assert.Equal("customer-123", principal.GetSubject());
        Assert.Equal("booking-web", principal.GetClientId());
        Assert.Equal("tenant-456", principal.GetTenantId());
    }

    [Fact]
    public void HelpersSupportCommonClientAndTenantAliases()
    {
        var principal = Principal(
            new Claim("azp", "booking-native"),
            new Claim("tid", "tenant-456"));

        Assert.Equal("booking-native", principal.GetClientId());
        Assert.Equal("tenant-456", principal.GetTenantId());
    }

    [Fact]
    public void HelpersFailClosedForConflictingClaimValues()
    {
        var principal = Principal(
            new Claim("sub", "customer-123"),
            new Claim("sub", "customer-999"),
            new Claim("client_id", "booking-web"),
            new Claim("azp", "different-client"),
            new Claim("tenant_id", "tenant-456"),
            new Claim("tid", "tenant-999"));

        Assert.Null(principal.GetSubject());
        Assert.Null(principal.GetClientId());
        Assert.Null(principal.GetTenantId());
    }

    [Fact]
    public void HelpersAllowDuplicateClaimsOnlyWhenValuesAgree()
    {
        var principal = Principal(
            new Claim("sub", "customer-123"),
            new Claim("sub", "customer-123"),
            new Claim("client_id", "booking-web"),
            new Claim("azp", "booking-web"));

        Assert.Equal("customer-123", principal.GetSubject());
        Assert.Equal("booking-web", principal.GetClientId());
    }

    [Fact]
    public void HelpersIgnoreClaimsFromUnauthenticatedIdentities()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(authenticationType: "test"),
            new ClaimsIdentity(
            [
                new Claim("sub", "attacker-subject"),
                new Claim("client_id", "attacker-client"),
                new Claim("tenant_id", "attacker-tenant")
            ])
        ]);

        Assert.Null(principal.GetSubject());
        Assert.Null(principal.GetClientId());
        Assert.Null(principal.GetTenantId());
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
