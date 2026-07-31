using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
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
        Assert.Same(CustomerApplicationErrors.InvalidIdentityClaims, result.Error);
    }

    [Theory]
    [InlineData("given_name", 101)]
    [InlineData("family_name", 101)]
    public void OversizedProfileClaimReturnsInvalidIdentityClaimsError(
        string claimType,
        int length)
    {
        var principal = CreatePrincipal(
            new Claim("sub", "subject-123"),
            new Claim(claimType, new string('x', length)));

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsFailure);
        Assert.Same(CustomerApplicationErrors.InvalidIdentityClaims, result.Error);
    }

    [Fact]
    public void InvalidVerifiedEmailReturnsInvalidIdentityClaimsError()
    {
        var principal = CreatePrincipal(
            new Claim("sub", "subject-123"),
            new Claim("email", "not-an-email"),
            new Claim("email_verified", "true"));

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsFailure);
        Assert.Same(CustomerApplicationErrors.InvalidIdentityClaims, result.Error);
    }

    [Fact]
    public void ProfileClaimsAreNormalizedUsingCustomerDomainRules()
    {
        var principal = CreatePrincipal(
            new Claim("sub", "subject-123"),
            new Claim("given_name", " Ada "),
            new Claim("family_name", " Lovelace "),
            new Claim("email", " ADA@EXAMPLE.COM "),
            new Claim("email_verified", "true"));

        var result = CurrentIdentity.From(principal);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada", result.Value.GivenName);
        Assert.Equal("Lovelace", result.Value.FamilyName);
        Assert.Equal("ada@example.com", result.Value.Email);
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
