using System.Security.Claims;
using Microservices.Security;

namespace Microservices.Security.Tests;

public sealed class AccessTokenClaimsValidatorTests
{
    [Fact]
    public void AcceptsTokenFromConfiguredAuthorizedPartyWithRequiredClaims()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("sub", "user-123"),
            new Claim("iat", "1774692000"),
            new Claim("jti", "token-123"),
            new Claim("azp", "order-mobile"));

        var result = AccessTokenClaimsValidator.TryValidate(
            principal,
            Options(),
            out var failure);

        Assert.True(result);
        Assert.Null(failure);
    }

    [Fact]
    public void RejectsTokenMissingARequiredClaim()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("sub", "user-123"),
            new Claim("iat", "1774692000"),
            new Claim("azp", "order-mobile"));

        var result = AccessTokenClaimsValidator.TryValidate(
            principal,
            Options(),
            out var failure);

        Assert.False(result);
        Assert.Contains("jti", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTokenIssuedToAnUnapprovedClient()
    {
        var principal = AuthenticatedPrincipal(
            new Claim("sub", "user-123"),
            new Claim("iat", "1774692000"),
            new Claim("jti", "token-123"),
            new Claim("azp", "untrusted-client"));

        var result = AccessTokenClaimsValidator.TryValidate(
            principal,
            Options(),
            out var failure);

        Assert.False(result);
        Assert.Contains("unauthorized client", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMissingOrDuplicateAuthorizedParty()
    {
        var missing = AuthenticatedPrincipal(
            new Claim("sub", "user-123"),
            new Claim("iat", "1774692000"),
            new Claim("jti", "token-123"));
        var duplicate = AuthenticatedPrincipal(
            new Claim("sub", "user-123"),
            new Claim("iat", "1774692000"),
            new Claim("jti", "token-123"),
            new Claim("azp", "order-mobile"),
            new Claim("azp", "order-mobile"));

        Assert.False(AccessTokenClaimsValidator.TryValidate(
            missing,
            Options(),
            out var missingFailure));
        Assert.False(AccessTokenClaimsValidator.TryValidate(
            duplicate,
            Options(),
            out var duplicateFailure));
        Assert.Contains("exactly one", missingFailure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one", duplicateFailure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IgnoresClaimsFromUnauthenticatedSecondaryIdentity()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [
                new Claim("sub", "user-123"),
                new Claim("iat", "1774692000"),
                new Claim("jti", "token-123")
            ],
            authenticationType: "test"),
            new ClaimsIdentity(
            [
                new Claim("azp", "order-mobile")
            ])
        ]);

        var result = AccessTokenClaimsValidator.TryValidate(
            principal,
            Options(),
            out var failure);

        Assert.False(result);
        Assert.Contains("azp", failure, StringComparison.Ordinal);
    }

    private static ApiSecurityOptions Options() => new()
    {
        Audience = "order-api",
        ValidAuthorizedParties = ["order-mobile"],
        RequiredClaims = ["sub", "iat", "jti"]
    };

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
