using FluentValidation;
using Product.Api.Features.Products.Creating.V1;
using Product.Api.Features.Products.Deleting.V1;
using Product.Api.Features.Products.GettingById.V1;
using Product.Api.Features.Products.Listing.V1;
using Product.Api.Features.Products.Updating.V1;
using ProductAggregate = global::Product.Api.Domain.Product;

namespace Product.Api.Tests;

public sealed class ProductValidatorTests
{
    [Fact]
    public void CreateValidatorRejectsEveryMalformedProductField()
    {
        var command = new CreateProductCommand(
            string.Empty,
            string.Empty,
            new string('D', ProductAggregate.MaximumDescriptionLength + 1),
            -0.01m,
            "US");

        var result = new CreateProductValidator().Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Sku));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Name));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Description));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Price));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.CurrencyCode));
    }

    [Fact]
    public void CreateValidatorAcceptsZeroPriceAndMaximumLengths()
    {
        var command = new CreateProductCommand(
            new string('S', ProductAggregate.MaximumSkuLength),
            new string('N', ProductAggregate.MaximumNameLength),
            new string('D', ProductAggregate.MaximumDescriptionLength),
            0m,
            "USD");

        var result = new CreateProductValidator().Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateValidatorRejectsPriceWithMoreThanTwoFractionalDigits()
    {
        var command = new CreateProductCommand(
            "BOOK-001",
            "Domain-Driven Design",
            null,
            10.001m,
            "USD");

        var result = new CreateProductValidator().Validate(command);

        Assert.Single(result.Errors, failure => failure.PropertyName == nameof(command.Price));
    }

    [Fact]
    public void UpdateValidatorRequiresIdentityVersionAndValidFields()
    {
        var command = new UpdateProductCommand(
            Guid.Empty,
            0,
            string.Empty,
            string.Empty,
            null,
            ProductAggregate.MaximumPrice + 0.01m,
            "U1D");

        var result = new UpdateProductValidator().Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.ProductId));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.ExpectedVersion));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Sku));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Name));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Price));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.CurrencyCode));
    }

    [Fact]
    public void GetByIdValidatorRequiresProductId()
    {
        var query = new GetProductByIdQuery(Guid.Empty);

        var result = new GetProductByIdValidator().Validate(query);

        var failure = Assert.Single(result.Errors);
        Assert.Equal(nameof(query.ProductId), failure.PropertyName);
    }

    [Fact]
    public void DeleteValidatorRequiresProductIdAndPositiveVersion()
    {
        var command = new DeleteProductCommand(Guid.Empty, 0);

        var result = new DeleteProductValidator().Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.ProductId));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.ExpectedVersion));
    }

    [Theory]
    [InlineData(0, 20, nameof(ListProductsQuery.Page))]
    [InlineData(1, 0, nameof(ListProductsQuery.PageSize))]
    [InlineData(1, 101, nameof(ListProductsQuery.PageSize))]
    [InlineData(int.MaxValue, 100, nameof(ListProductsQuery.Page))]
    public void ListValidatorEnforcesBoundedPagination(
        int page,
        int pageSize,
        string expectedProperty)
    {
        var query = new ListProductsQuery(page, pageSize);

        var result = new ListProductsValidator().Validate(query);

        var failure = Assert.Single(result.Errors);
        Assert.Equal(expectedProperty, failure.PropertyName);
    }
}
