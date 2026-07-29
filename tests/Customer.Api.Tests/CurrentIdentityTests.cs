using System.Security.Claims;
using Customer.Api.Infrastructure;

namespace Customer.Api.Tests;

public sealed class CurrentIdentityTests
{
    [Fact]
    public void VerifiedEmailIsAvailableForInitialCustomerProvisioning()
    {
        var principal = CreatePrincipal(
            new Claim("sub", "subject-123"),
            new Claim("given_name", "Ada"),
            new Claim("family_name", "Lovelace"),
            new Claim("email", "ada@example.com"),
            new Claim("email_verified", "true"));

        var identity = CurrentIdentity.From(principal);

        Assert.Equal("keycloak", identity.Provider);
        Assert.Equal("subject-123", identity.Subject);
        Assert.Equal("Ada", identity.GivenName);
        Assert.Equal("Lovelace", identity.FamilyName);
        Assert.Equal("ada@example.com", identity.Email);
    }

    [Fact]
    public void UnverifiedEmailIsNotCopiedIntoCustomerData()
    {
        var principal = CreatePrincipal(
            new Claim("sub", "subject-123"),
            new Claim("email", "unverified@example.com"),
            new Claim("email_verified", "false"));

        var identity = CurrentIdentity.From(principal);

        Assert.Null(identity.Email);
    }

    [Fact]
    public void MissingSubjectIsRejected()
    {
        var principal = CreatePrincipal(new Claim("email_verified", "true"));

        Assert.Throws<UnauthorizedAccessException>(() => CurrentIdentity.From(principal));
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
