using Inventory.Api.Domain;
using Inventory.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Tests;

public sealed class InventoryPersistenceIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("INVENTORY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("INVENTORY_TEST_CONNECTION_STRING is required for Inventory integration tests.");

    [Fact]
    public async Task ConcurrentReservationsCannotBothCommitAgainstSameVersion()
    {
        await ResetDatabaseAsync(); var productId = Guid.NewGuid();
        await using (var setup = CreateContext()) { setup.InventoryItems.Add(InventoryItem.Create(productId, 5, DateTimeOffset.UtcNow).Value); await setup.SaveChangesAsync(); }
        await using var first = CreateContext(); await using var second = CreateContext();
        var firstItem = await first.InventoryItems.SingleAsync(item => item.ProductId == productId); var secondItem = await second.InventoryItems.SingleAsync(item => item.ProductId == productId);
        Assert.True(firstItem.Reserve(4, DateTimeOffset.UtcNow).IsSuccess); Assert.True(secondItem.Reserve(4, DateTimeOffset.UtcNow).IsSuccess);
        await first.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verification = CreateContext(); var persisted = await verification.InventoryItems.SingleAsync(item => item.ProductId == productId); Assert.Equal(4, persisted.Reserved); Assert.Equal(1, persisted.Available);
    }

    private static async Task ResetDatabaseAsync() { await using var db = CreateContext(); await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync(); }
    private static InventoryDbContext CreateContext() => new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(ConnectionString).Options);
}
