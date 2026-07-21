using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Microservices.Persistence.Postgres;

public static class PostgresExtensions
{
    public static IServiceCollection AddPostgresDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName,
        Action<DbContextOptionsBuilder>? configureOptions = null,
        Action<NpgsqlDbContextOptionsBuilder>? configurePostgres = null)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionName}' is not configured.");

        services.AddDbContextPool<TContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
            {
                postgres.EnableRetryOnFailure(maxRetryCount: 5);
                configurePostgres?.Invoke(postgres);
            });
            configureOptions?.Invoke(options);
        });

        return services;
    }
}
