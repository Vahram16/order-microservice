using Customer.Api.Domain;
using CustomerAggregate = global::Customer.Api.Domain.Customer;

namespace Customer.Api.Tests;

public sealed class CustomerDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RegisterNormalizesIdentityAndClaimBackedDetails()
    {
        var customer = CustomerAggregate.Register(
            " Keycloak ",
            " subject-123 ",
            " Ada ",
            " Lovelace ",
            " ADA@EXAMPLE.COM ",
            Now);

        Assert.Equal("keycloak", customer.IdentityProvider);
        Assert.Equal("subject-123", customer.IdentitySubject);
        Assert.Equal("Ada", customer.FirstName);
        Assert.Equal("Lovelace", customer.LastName);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal(1, customer.Version);
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

        Assert.False(first.IsDefaultShipping);
        Assert.True(second.IsDefaultShipping);
        Assert.Equal(3, customer.Version);
    }

    [Fact]
    public void InvalidAddressUpdateDoesNotPartiallyMutateAggregate()
    {
        var customer = CreateCustomer();
        var address = customer.AddAddress(
            Guid.NewGuid(),
            CreateAddress("Home", defaultShipping: true),
            Now);
        var invalid = CreateAddress("Office", defaultShipping: false) with
        {
            CountryCode = "INVALID"
        };

        Assert.Throws<CustomerDomainException>(() =>
            customer.UpdateAddress(address.Id, invalid, Now.AddMinutes(1)));

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

        var exception = Assert.Throws<CustomerDomainException>(() =>
            customer.AddAddress(Guid.NewGuid(), invalid, Now));

        Assert.Equal("customer.invalid_country_code", exception.Code);
    }

    [Fact]
    public void AddressCollectionCannotBeCastToMutableList()
    {
        var customer = CreateCustomer();
        customer.AddAddress(Guid.NewGuid(), CreateAddress("Home"), Now);

        Assert.False(customer.Addresses is List<CustomerAddress>);
    }

    [Fact]
    public void ReusingAddressIdWithDifferentPayloadIsRejected()
    {
        var customer = CreateCustomer();
        var addressId = Guid.NewGuid();
        customer.AddAddress(addressId, CreateAddress("Home"), Now);

        Assert.Throws<CustomerIdempotencyConflictException>(() =>
            customer.AddAddress(addressId, CreateAddress("Office"), Now.AddMinutes(1)));
    }

    [Fact]
    public void UpdatingAddressCannotTargetAnotherAggregateAddress()
    {
        var customer = CreateCustomer();

        var exception = Assert.Throws<CustomerAddressNotFoundException>(() =>
            customer.UpdateAddress(Guid.NewGuid(), CreateAddress("Unknown"), Now.AddMinutes(1)));

        Assert.Contains("was not found", exception.Message);
    }

    [Fact]
    public void CustomerCannotSaveMoreThanConfiguredMaximum()
    {
        var customer = CreateCustomer();
        for (var index = 0; index < CustomerAggregate.MaximumSavedAddresses; index++)
        {
            customer.AddAddress(
                Guid.NewGuid(),
                CreateAddress($"Address {index}"),
                Now.AddMinutes(index));
        }

        Assert.Throws<CustomerDomainException>(() =>
            customer.AddAddress(Guid.NewGuid(), CreateAddress("Too many"), Now.AddHours(1)));
    }

    [Fact]
    public void AuditTimestampDoesNotMoveBackwards()
    {
        var customer = CreateCustomer();

        customer.UpdateDetails(
            "Grace",
            "Hopper",
            "grace@example.com",
            null,
            Now.AddMinutes(-1));

        Assert.Equal(Now, customer.UpdatedAt);
        Assert.Equal(2, customer.Version);
    }

    [Fact]
    public void ClosingAccountAnonymizesCustomerOwnedPii()
    {
        var customer = CreateCustomer();
        customer.AddAddress(Guid.NewGuid(), CreateAddress("Home"), Now);

        customer.CloseAccount(Now.AddMinutes(1));

        Assert.Equal(CustomerStatus.Deactivated, customer.Status);
        Assert.Null(customer.FirstName);
        Assert.Null(customer.LastName);
        Assert.Null(customer.Email);
        Assert.Null(customer.PhoneNumber);
        Assert.Empty(customer.Addresses);
    }

    private static CustomerAggregate CreateCustomer() =>
        CustomerAggregate.Register(
            "keycloak",
            "subject-123",
            "Ada",
            "Lovelace",
            "ada@example.com",
            Now);

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
