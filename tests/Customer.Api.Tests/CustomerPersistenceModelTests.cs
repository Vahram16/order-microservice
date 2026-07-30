using Customer.Api.Domain;
using Customer.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Tests;

public sealed class CustomerPersistenceModelTests
{
    [Fact]
    public void ResultMappedConstraintNamesMustMatchTheEfModel()
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=customer_model_tests;Username=unused;Password=unused")
            .Options;
        using var dbContext = new CustomerDbContext(options);

        var customer = dbContext.Model.FindEntityType(typeof(Domain.Customer));
        Assert.NotNull(customer);
        var identity = Assert.Single(
            customer.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(Domain.Customer.IdentityProvider),
                    nameof(Domain.Customer.IdentitySubject)
                ]));
        Assert.Equal(
            CustomerDatabaseConstraints.Identity,
            identity.GetDatabaseName());

        var address = dbContext.Model.FindEntityType(typeof(CustomerAddress));
        Assert.NotNull(address);
        Assert.Equal(
            CustomerDatabaseConstraints.AddressPrimaryKey,
            address.FindPrimaryKey()?.GetName());
        Assert.Contains(
            address.GetIndexes(),
            index =>
                index.GetDatabaseName() == CustomerDatabaseConstraints.DefaultShipping &&
                index.IsUnique);
        Assert.Contains(
            address.GetIndexes(),
            index =>
                index.GetDatabaseName() == CustomerDatabaseConstraints.DefaultBilling &&
                index.IsUnique);
    }
}
