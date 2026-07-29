using Customer.Api.Domain;
using CustomerAggregate = Customer.Api.Domain.Customer;

namespace Customer.Api.Tests;

public sealed class CustomerDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RegisterNormalizesIdentityAndClaimBackedDetails()
    {
        var customer = CustomerAggregate.Register(
            " keycloak ",
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
        var first = customer.AddAddress(CreateAddress("Home", defaultShipping: true), Now);
        var second = customer.AddAddress(
            CreateAddress("Office", defaultShipping: true),
            Now.AddMinutes(1));

        Assert.False(first.IsDefaultShipping);
        Assert.True(second.IsDefaultShipping);
        Assert.Equal(3, customer.Version);
    }

    [Fact]
    public void UpdatingAddressCannotTargetAnotherAggregateAddress()
    {
        var customer = CreateCustomer();

        var exception = Assert.Throws<CustomerAddressNotFoundException>(() =>
            customer.UpdateAddress(Guid.NewGuid(), CreateAddress("Unknown"), Now.AddMinutes(1)));

        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerCannotSaveMoreThanConfiguredMaximum()
    {
        var customer = CreateCustomer();
        for (var index = 0; index < CustomerAggregate.MaximumSavedAddresses; index++)
        {
            customer.AddAddress(CreateAddress($"Address {index}"), Now.AddMinutes(index));
        }

        Assert.Throws<CustomerDomainException>(() =>
            customer.AddAddress(CreateAddress("Too many"), Now.AddHours(1)));
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
