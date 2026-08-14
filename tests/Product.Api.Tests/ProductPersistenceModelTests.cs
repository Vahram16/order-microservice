using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Product.Api.Persistence;

namespace Product.Api.Tests;

public sealed class ProductPersistenceModelTests
{
    [Fact]
    public void ProductModelPreservesIdentityMoneyAndConcurrencyContracts()
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=product_model_tests;Username=unused;Password=unused")
            .Options;
        using var dbContext = new ProductDbContext(options);

        var product = dbContext.Model.FindEntityType(typeof(Domain.Product));
        Assert.NotNull(product);

        var id = product.FindProperty(nameof(Domain.Product.Id));
        Assert.NotNull(id);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);

        var skuIndex = Assert.Single(
            product.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Domain.Product.Sku)]));
        Assert.True(skuIndex.IsUnique);
        Assert.Equal(ProductDatabaseConstraints.Sku, skuIndex.GetDatabaseName());

        var price = product.FindProperty(nameof(Domain.Product.Price));
        Assert.NotNull(price);
        Assert.Equal(18, price.GetPrecision());
        Assert.Equal(2, price.GetScale());

        var currencyCode = product.FindProperty(nameof(Domain.Product.CurrencyCode));
        Assert.NotNull(currencyCode);
        Assert.Equal(3, currencyCode.GetMaxLength());

        var version = product.FindProperty(nameof(Domain.Product.Version));
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
    }
}
