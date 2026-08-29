using Microsoft.EntityFrameworkCore;
using Order.Api.Persistence;

namespace Order.Api.Tests;

public sealed class OrderPersistenceIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("ORDER_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ORDER_TEST_CONNECTION_STRING is required for Order integration tests.");

    [Fact]
    public async Task ConcurrentSameIdempotencyKeyHasSingleDatabaseWinner()
    {
        await ResetDatabaseAsync(); var customerId = Guid.NewGuid(); var key = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await using var first = CreateContext(); await using var second = CreateContext();
        first.OrderSubmissions.Add(OrderSubmission.Create(customerId, key, new string('A', 64), Guid.NewGuid(), now));
        second.OrderSubmissions.Add(OrderSubmission.Create(customerId, key, new string('A', 64), Guid.NewGuid(), now));
        var results = await Task.WhenAll(TrySaveAsync(first), TrySaveAsync(second));
        Assert.Single(results, value => value); Assert.Single(results, value => !value);
        await using var verification = CreateContext(); Assert.Equal(1, await verification.OrderSubmissions.CountAsync());
    }

    private static async Task<bool> TrySaveAsync(OrderDbContext dbContext) { try { await dbContext.SaveChangesAsync(); return true; } catch (DbUpdateException) { return false; } }
    private static async Task ResetDatabaseAsync() { await using var db = CreateContext(); await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync(); }
    private static OrderDbContext CreateContext() => new(new DbContextOptionsBuilder<OrderDbContext>().UseNpgsql(ConnectionString).Options);
}
