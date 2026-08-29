using Microservices.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Product.Api.Features.Products.Updating.V1;
using Product.Api.Persistence;
using ProductAggregate = global::Product.Api.Domain.Product;

namespace Product.Api.Tests;

public sealed class ProductConcurrencyIntegrationTests(ProductApiFactory factory)
    : IClassFixture<ProductApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConcurrentUpdateRaceTranslatesDatabaseConflictToVersionMismatch()
    {
        var productId = await SeedProductAsync();
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstDbContext = firstScope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var secondDbContext = secondScope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var firstEventPublisher = firstScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var secondEventPublisher = secondScope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        var firstCopy = await firstDbContext.Products.SingleAsync(product => product.Id == productId);
        var secondCopy = await secondDbContext.Products.SingleAsync(product => product.Id == productId);
        Assert.Equal(1, firstCopy.Version);
        Assert.Equal(1, secondCopy.Version);

        var firstHandler = new UpdateProductHandler(firstDbContext, firstEventPublisher, TimeProvider.System);
        var secondHandler = new UpdateProductHandler(secondDbContext, secondEventPublisher, TimeProvider.System);
        var firstResult = await firstHandler.Handle(
            new UpdateProductCommand(
                productId,
                1,
                "BOOK-FIRST",
                "First concurrent update",
                null,
                50m,
                "USD"),
            CancellationToken.None);

        var secondResult = await secondHandler.Handle(
            new UpdateProductCommand(
                productId,
                1,
                "BOOK-SECOND",
                "Second concurrent update",
                null,
                60m,
                "USD"),
            CancellationToken.None);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsFailure);
        Assert.Equal("product.version_mismatch", secondResult.Error.Code);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDbContext =
            verificationScope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var persisted = await verificationDbContext.Products
            .AsNoTracking()
            .SingleAsync(product => product.Id == productId);
        Assert.Equal("BOOK-FIRST", persisted.Sku);
        Assert.Equal("First concurrent update", persisted.Name);
        Assert.Equal(2, persisted.Version);
    }

    private async Task<Guid> SeedProductAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var creation = ProductAggregate.Create(
            "BOOK-001",
            "Domain-Driven Design",
            null,
            49.99m,
            "USD",
            DateTimeOffset.UtcNow);
        Assert.True(creation.IsSuccess);
        dbContext.Products.Add(creation.Value);
        await dbContext.SaveChangesAsync();
        return creation.Value.Id;
    }
}
