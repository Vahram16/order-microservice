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

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsSuccess);
        var identity = result.Value;
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

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Email);
    }

    [Fact]
    public void MissingSubjectReturnsAuthenticationError()
    {
        var principal = CreatePrincipal(new Claim("email_verified", "true"));

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsFailure);
        Assert.Equal("customer.authentication_required", result.Error.Code);
    }

    [Fact]
    public void OversizedSubjectReturnsInvalidIdentityClaimsError()
    {
        var principal = CreatePrincipal(new Claim("sub", new string('s', 256)));

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsFailure);
        Assert.Equal("customer.invalid_identity_claims", result.Error.Code);
    }

    [Fact]
    public void SubjectWithSurroundingWhitespaceIsRejectedRatherThanNormalized()
    {
        var principal = CreatePrincipal(new Claim("sub", " subject-123 "));

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsFailure);
        Assert.Equal("customer.invalid_identity_claims", result.Error.Code);
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
