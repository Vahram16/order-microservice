using System.Security.Claims;
using Microservices.Security;

namespace Microservices.Security.Tests;

public sealed class KeycloakRoleClaimsMapperTests
{
    [Fact]
    public void MapsOnlyConfiguredClientRoles()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(
                "resource_access",
                """
                {
                  "order-api": { "roles": ["order-user", "order-manager"] },
                  "account": { "roles": ["manage-account"] }
                }
                """));
        var options = new ApiSecurityOptions
        {
            Audience = "order-api",
            RoleClientId = "order-api"
        };

        KeycloakRoleClaimsMapper.MapRoles(principal, options);

        Assert.Equal(
            ["order-manager", "order-user"],
            Roles(principal).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("manage-account", Roles(principal));
    }

    [Fact]
    public void FallsBackToAudienceForClientRoleMapping()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(
                "resource_access",
                """{"order-api":{"roles":["order-admin"]}}"""));
        var options = new ApiSecurityOptions
        {
            Audience = "order-api"
        };

        KeycloakRoleClaimsMapper.MapRoles(principal, options);

        Assert.Contains("order-admin", Roles(principal));
    }

    [Fact]
    public void MapsRealmRolesOnlyWhenExplicitlyEnabled()
    {
        const string realmAccess = """{"roles":["platform-support"]}""";
        var defaultPrincipal = AuthenticatedPrincipal(
            new Claim("realm_access", realmAccess));
        var enabledPrincipal = AuthenticatedPrincipal(
            new Claim("realm_access", realmAccess));

        KeycloakRoleClaimsMapper.MapRoles(
            defaultPrincipal,
            new ApiSecurityOptions { Audience = "order-api" });
        KeycloakRoleClaimsMapper.MapRoles(
            enabledPrincipal,
            new ApiSecurityOptions
            {
                Audience = "order-api",
                MapRealmRoles = true
            });

        Assert.DoesNotContain("platform-support", Roles(defaultPrincipal));
        Assert.Contains("platform-support", Roles(enabledPrincipal));
    }

    [Fact]
    public void MalformedRoleClaimsGrantNothingAndMappingIsIdempotent()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("resource_access", "not-json"),
            new Claim(
                "resource_access",
                """{"order-api":{"roles":["order-user","order-user",null,42]}}"""),
            new Claim(SecurityClaimTypes.Role, "order-user"));
        var options = new ApiSecurityOptions
        {
            Audience = "order-api"
        };

        KeycloakRoleClaimsMapper.MapRoles(principal, options);
        KeycloakRoleClaimsMapper.MapRoles(principal, options);

        Assert.Equal(["order-user"], Roles(principal));
    }

    [Fact]
    public void DoesNotModifyUnauthenticatedPrincipal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(
                "resource_access",
                """{"order-api":{"roles":["order-admin"]}}""")
        ]));

        KeycloakRoleClaimsMapper.MapRoles(
            principal,
            new ApiSecurityOptions { Audience = "order-api" });

        Assert.Empty(Roles(principal));
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    private static string[] Roles(ClaimsPrincipal principal) =>
        principal.FindAll(SecurityClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToArray();
}
