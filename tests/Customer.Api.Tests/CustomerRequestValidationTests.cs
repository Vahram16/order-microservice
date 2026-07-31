using Customer.Api.Domain;
using Customer.Api.Features.Customers.AddingAddress.V1;
using Customer.Api.Features.Customers.UpdatingAddress.V1;
using FluentValidation;
using FluentValidation.Results;

namespace Customer.Api.Tests;

public sealed class CustomerRequestValidationTests
{
    [Fact]
    public void AddAddressValidationUsesFlatRequestPropertyNames()
    {
        var result = new AddCustomerAddressValidator().Validate(
            new AddCustomerAddressCommand(
                "provider",
                "subject",
                1,
                Guid.NewGuid(),
                InvalidAddress()));

        AssertPublicAddressFailures(result);
    }

    [Fact]
    public void UpdateAddressValidationUsesFlatRequestPropertyNames()
    {
        var result = new UpdateCustomerAddressValidator().Validate(
            new UpdateCustomerAddressCommand(
                "provider",
                "subject",
                1,
                Guid.NewGuid(),
                InvalidAddress()));

        AssertPublicAddressFailures(result);
    }

    private static AddressData InvalidAddress() =>
        new(
            null,
            string.Empty,
            "12 Computing Street",
            null,
            "London",
            null,
            "SW1A 1AA",
            string.Empty,
            null,
            true,
            false);

    private static void AssertPublicAddressFailures(ValidationResult result)
    {
        Assert.Contains(result.Errors, failure => failure.PropertyName == "RecipientName");
        Assert.DoesNotContain(
            result.Errors,
            failure => failure.PropertyName.StartsWith("Address.", StringComparison.Ordinal));
        Assert.Single(
            result.Errors,
            failure => failure.PropertyName == "CountryCode");
    }
}
