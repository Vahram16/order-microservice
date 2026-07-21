using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Identity.Api.Persistence;

public sealed class IdentityServiceDbContextFactory
    : IDesignTimeDbContextFactory<IdentityServiceDbContext>
{
    public IdentityServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__identity-db") ??
            "Host=localhost;Port=5432;Database=identity;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<IdentityServiceDbContext>();
        builder.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable("__ef_migrations_history", "identity"));
        builder.UseOpenIddict<Guid>();

        return new IdentityServiceDbContext(builder.Options);
    }
}
