using ProductAggregate = global::Product.Api.Domain.Product;

namespace Product.Api.Tests;

public sealed class ProductDomainTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateNormalizesTextAndInitializesVersion()
    {
        var result = ProductAggregate.Create(
            " book-001 ",
            " Domain-Driven Design ",
            " A practical guide ",
            49.99m,
            " usd ",
            Now);

        Assert.True(result.IsSuccess);
        var product = result.Value;
        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("BOOK-001", product.Sku);
        Assert.Equal("Domain-Driven Design", product.Name);
        Assert.Equal("A practical guide", product.Description);
        Assert.Equal(49.99m, product.Price);
        Assert.Equal("USD", product.CurrencyCode);
        Assert.Equal(Now, product.CreatedAt);
        Assert.Equal(Now, product.UpdatedAt);
        Assert.Equal(1, product.Version);
    }

    [Fact]
    public void CreateAcceptsZeroPrice()
    {
        var result = ProductAggregate.Create(
            "BOOK-001",
            "Domain-Driven Design",
            null,
            0m,
            "USD",
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.Price);
    }

    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public void CreateRejectsInvalidPrice(decimal price)
    {
        var result = ProductAggregate.Create(
            "BOOK-001",
            "Domain-Driven Design",
            null,
            price,
            "USD",
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("product.invalid_price", result.Error.Code);
        Assert.Equal("price", result.Error.Metadata["field"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U1D")]
    public void CreateRejectsInvalidCurrencyCode(string currencyCode)
    {
        var result = ProductAggregate.Create(
            "BOOK-001",
            "Domain-Driven Design",
            null,
            49.99m,
            currencyCode,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("product.invalid_currency_code", result.Error.Code);
        Assert.Equal("currencyCode", result.Error.Metadata["field"]);
    }

    [Fact]
    public void CreateAcceptsConfiguredMaximumLengthsAndPrice()
    {
        var result = ProductAggregate.Create(
            new string('S', ProductAggregate.MaximumSkuLength),
            new string('N', ProductAggregate.MaximumNameLength),
            new string('D', ProductAggregate.MaximumDescriptionLength),
            ProductAggregate.MaximumPrice,
            "USD",
            Now);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("sku")]
    [InlineData("name")]
    [InlineData("description")]
    public void CreateRejectsTextBeyondConfiguredMaximum(string field)
    {
        var sku = field == "sku"
            ? new string('S', ProductAggregate.MaximumSkuLength + 1)
            : "BOOK-001";
        var name = field == "name"
            ? new string('N', ProductAggregate.MaximumNameLength + 1)
            : "Domain-Driven Design";
        var description = field == "description"
            ? new string('D', ProductAggregate.MaximumDescriptionLength + 1)
            : null;

        var result = ProductAggregate.Create(sku, name, description, 49.99m, "USD", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("product.validation", result.Error.Code);
        Assert.Equal(field, result.Error.Metadata["field"]);
    }

    [Fact]
    public void UpdateReplacesMutableDetailsAndAdvancesVersion()
    {
        var product = CreateProduct();

        var result = product.Update(
            "BOOK-002",
            "Implementing Domain-Driven Design",
            null,
            59.95m,
            "EUR",
            Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal("BOOK-002", product.Sku);
        Assert.Equal("Implementing Domain-Driven Design", product.Name);
        Assert.Null(product.Description);
        Assert.Equal(59.95m, product.Price);
        Assert.Equal("EUR", product.CurrencyCode);
        Assert.Equal(Now.AddMinutes(1), product.UpdatedAt);
        Assert.Equal(2, product.Version);
    }

    [Fact]
    public void FailedUpdateLeavesAggregateUnchanged()
    {
        var product = CreateProduct();

        var result = product.Update(
            "BOOK-002",
            "Implementing Domain-Driven Design",
            "Changed description",
            -0.01m,
            "EUR",
            Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("BOOK-001", product.Sku);
        Assert.Equal("Domain-Driven Design", product.Name);
        Assert.Equal("A practical guide", product.Description);
        Assert.Equal(49.99m, product.Price);
        Assert.Equal("USD", product.CurrencyCode);
        Assert.Equal(Now, product.UpdatedAt);
        Assert.Equal(1, product.Version);
    }

    [Fact]
    public void ExpectedVersionMustMatchCurrentPositiveVersion()
    {
        var product = CreateProduct();

        var current = product.EnsureExpectedVersion(1);
        var stale = product.EnsureExpectedVersion(2);
        var invalid = product.EnsureExpectedVersion(0);

        Assert.True(current.IsSuccess);
        Assert.True(stale.IsFailure);
        Assert.True(invalid.IsFailure);
        Assert.Equal("product.version_mismatch", stale.Error.Code);
        Assert.Equal("product.version_mismatch", invalid.Error.Code);
    }

    [Fact]
    public void UpdatedTimestampDoesNotMoveBackwards()
    {
        var product = CreateProduct();

        var result = product.Update(
            product.Sku,
            product.Name,
            product.Description,
            product.Price,
            product.CurrencyCode,
            Now.AddMinutes(-1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, product.UpdatedAt);
        Assert.Equal(2, product.Version);
    }

    private static ProductAggregate CreateProduct()
    {
        var result = ProductAggregate.Create(
            "BOOK-001",
            "Domain-Driven Design",
            "A practical guide",
            49.99m,
            "USD",
            Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    public static TheoryData<decimal> InvalidPrices => new()
    {
        -0.01m,
        0.001m,
        ProductAggregate.MaximumPrice + 0.01m
    };
}
