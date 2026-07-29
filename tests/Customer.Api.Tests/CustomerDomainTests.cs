using Customer.Api.Domain;
using CustomerAggregate = global::Customer.Api.Domain.Customer;

namespace Customer.Api.Tests;

public sealed class CustomerDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RegisterNormalizesProviderAndClaimBackedDetailsButPreservesOpaqueSubject()
    {
        var result = CustomerAggregate.Register(
            " Keycloak ",
            "subject-123",
            " Ada ",
            " Lovelace ",
            " ADA@EXAMPLE.COM ",
            Now);

        Assert.True(result.IsSuccess);
        var customer = result.Value;
        Assert.Equal("keycloak", customer.IdentityProvider);
        Assert.Equal("subject-123", customer.IdentitySubject);
        Assert.Equal("Ada", customer.FirstName);
        Assert.Equal("Lovelace", customer.LastName);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal(1, customer.Version);
    }

    [Fact]
    public void RegisterRejectsWhitespaceAroundOpaqueSubject()
    {
        var result = CustomerAggregate.Register(
            "keycloak",
            " subject-123 ",
            null,
            null,
            null,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("customer.validation", result.Error.Code);
        Assert.Equal("identitySubject", result.Error.Metadata["field"]);
    }

    [Fact]
    public void AddingNewDefaultShippingAddressClearsPreviousDefault()
    {
        var customer = CreateCustomer();
        var first = customer.AddAddress(Guid.NewGuid(), CreateAddress("Home", defaultShipping: true), Now);
        var second = customer.AddAddress(
            Guid.NewGuid(),
            CreateAddress("Office", defaultShipping: true),
            Now.AddMinutes(1));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(first.Value.IsDefaultShipping);
        Assert.True(second.Value.IsDefaultShipping);
        Assert.Equal(3, customer.Version);
    }

    [Fact]
    public void InvalidAddressUpdateDoesNotPartiallyMutateAggregate()
    {
        var customer = CreateCustomer();
        var addressResult = customer.AddAddress(
            Guid.NewGuid(),
            CreateAddress("Home", defaultShipping: true),
            Now);
        Assert.True(addressResult.IsSuccess);
        var address = addressResult.Value;
        var invalid = CreateAddress("Office", defaultShipping: false) with
        {
            CountryCode = "INVALID"
        };

        var update = customer.UpdateAddress(address.Id, invalid, Now.AddMinutes(1));

        Assert.True(update.IsFailure);
        Assert.Equal("customer.invalid_country_code", update.Error.Code);
        Assert.Equal("Home", address.Label);
        Assert.Equal("GB", address.CountryCode.Value);
        Assert.True(address.IsDefaultShipping);
        Assert.Equal(2, customer.Version);
    }

    [Theory]
    [InlineData("G")]
    [InlineData("1B")]
    [InlineData("GBR")]
    public void CountryCodeInvariantIsOwnedByDomain(string value)
    {
        var customer = CreateCustomer();
        var invalid = CreateAddress("Invalid") with { CountryCode = value };

        var result = customer.AddAddress(Guid.NewGuid(), invalid, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("customer.invalid_country_code", result.Error.Code);
    }

    [Fact]
    public void AddressCollectionCannotBeCastToMutableList()
    {
        var customer = CreateCustomer();
        var add = customer.AddAddress(Guid.NewGuid(), CreateAddress("Home"), Now);

        Assert.True(add.IsSuccess);
        Assert.False(customer.Addresses is List<CustomerAddress>);
    }

    [Fact]
    public void ReusingAddressIdWithDifferentPayloadIsDomainIdentityConflict()
    {
        var customer = CreateCustomer();
        var addressId = Guid.NewGuid();
        var first = customer.AddAddress(addressId, CreateAddress("Home"), Now);

        var retry = customer.AddAddress(addressId, CreateAddress("Office"), Now.AddMinutes(1));

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsFailure);
        Assert.Equal("customer.address_identity_conflict", retry.Error.Code);
    }

    [Fact]
    public void UpdatingAddressCannotTargetAnotherAggregateAddress()
    {
        var customer = CreateCustomer();

        var result = customer.UpdateAddress(
            Guid.NewGuid(),
            CreateAddress("Unknown"),
            Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("customer.address_not_found", result.Error.Code);
    }

    [Fact]
    public void CustomerCannotSaveMoreThanConfiguredMaximum()
    {
        var customer = CreateCustomer();
        for (var index = 0; index < CustomerAggregate.MaximumSavedAddresses; index++)
        {
            var add = customer.AddAddress(
                Guid.NewGuid(),
                CreateAddress($"Address {index}"),
                Now.AddMinutes(index));
            Assert.True(add.IsSuccess);
        }

        var overflow = customer.AddAddress(
            Guid.NewGuid(),
            CreateAddress("Too many"),
            Now.AddHours(1));

        Assert.True(overflow.IsFailure);
        Assert.Equal("customer.address_limit_exceeded", overflow.Error.Code);
        Assert.Equal(CustomerAggregate.MaximumSavedAddresses, overflow.Error.Metadata["maximum"]);
    }

    [Fact]
    public void AuditTimestampDoesNotMoveBackwards()
    {
        var customer = CreateCustomer();

        var update = customer.UpdateDetails(
            "Grace",
            "Hopper",
            "grace@example.com",
            null,
            Now.AddMinutes(-1));

        Assert.True(update.IsSuccess);
        Assert.Equal(Now, customer.UpdatedAt);
        Assert.Equal(2, customer.Version);
    }

    [Fact]
    public void ClosingAccountAnonymizesCustomerOwnedPii()
    {
        var customer = CreateCustomer();
        var add = customer.AddAddress(Guid.NewGuid(), CreateAddress("Home"), Now);

        var close = customer.CloseAccount(Now.AddMinutes(1));

        Assert.True(add.IsSuccess);
        Assert.True(close.IsSuccess);
        Assert.True(close.Value);
        Assert.Equal(CustomerStatus.Deactivated, customer.Status);
        Assert.Null(customer.FirstName);
        Assert.Null(customer.LastName);
        Assert.Null(customer.Email);
        Assert.Null(customer.PhoneNumber);
        Assert.Empty(customer.Addresses);
    }

    [Fact]
    public void FailedDetailUpdateLeavesAggregateUnchanged()
    {
        var customer = CreateCustomer();

        var result = customer.UpdateDetails(
            "Grace",
            "Hopper",
            "not-an-email",
            "+1 555 0100",
            Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("customer.invalid_email", result.Error.Code);
        Assert.Equal("Ada", customer.FirstName);
        Assert.Equal("Lovelace", customer.LastName);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Null(customer.PhoneNumber);
        Assert.Equal(1, customer.Version);
    }

    [Fact]
    public void InvalidAuditContractThrowsInsteadOfReturningClientError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CustomerAuditEntry.Create(
            Guid.NewGuid(),
            "subject-123",
            CustomerAuditActions.DetailsUpdated,
            Now,
            0));
    }

    private static CustomerAggregate CreateCustomer()
    {
        var result = CustomerAggregate.Register(
            "keycloak",
            "subject-123",
            "Ada",
            "Lovelace",
            "ada@example.com",
            Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AddressData CreateAddress(
        string label,
        bool defaultShipping = false,
        bool defaultBilling = false) => new(
        label,
        "Ada Lovelace",
        "12 Computing Street",
        null,
        "London",
        null,
        "SW1A 1AA",
        "gb",
        "+44 20 0000 0000",
        defaultShipping,
        defaultBilling);
}
