using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Microservices.Primitives;
using Microsoft.AspNetCore.Http;

namespace Customer.Api.Tests;

public sealed class CustomerHttpResultsTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("status")]
    [InlineData("traceId")]
    public void PublicMetadataCannotOverrideProblemContract(string metadataKey)
    {
        var error = OperationError.InvalidInput(
            "customer.validation",
            "Invalid customer value.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [metadataKey] = "spoofed"
            });
        var context = new DefaultHttpContext();

        Assert.Throws<InvalidOperationException>(() =>
            CustomerHttpResults.Problem(error, context));
    }

    [Fact]
    public void DefaultAddressPersistenceRacesArePreconditionFailures()
    {
        var shipping = CustomerErrorCatalog.GetRequired(
            CustomerApplicationErrors.DefaultShippingConflict);
        var billing = CustomerErrorCatalog.GetRequired(
            CustomerApplicationErrors.DefaultBillingConflict);

        Assert.Equal(ErrorCategory.ConcurrencyConflict, shipping.Category);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, shipping.Status);
        Assert.Equal(ErrorCategory.ConcurrencyConflict, billing.Category);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, billing.Status);
    }

    [Fact]
    public void DomainInternalErrorsAreNotPublishedInHttpCatalog()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CustomerErrorCatalog.GetRequired(CustomerErrors.AddressIdentityConflict));
        Assert.Throws<InvalidOperationException>(() =>
            CustomerErrorCatalog.GetRequired(CustomerErrors.InvalidAddressId));
    }

    [Fact]
    public void InvalidIdentityClaimsProduceAuthenticationChallenge()
    {
        var context = new DefaultHttpContext();

        var result = CustomerHttpResults.Problem(
            CustomerApplicationErrors.InvalidIdentityClaims,
            context);
        var descriptor = CustomerErrorCatalog.GetRequired(
            CustomerApplicationErrors.InvalidIdentityClaims);

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, descriptor.Status);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate);
    }
}
