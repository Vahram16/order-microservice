using Microservices.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Persistence;

internal static class IdentityPersistenceRegistration
{
    public static void Add<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddPostgresDbContext<IdentityServiceDbContext>(
            builder.Configuration,
            "identity-db",
            options => options.UseOpenIddict<Guid>(),
            postgres => postgres.MigrationsHistoryTable(
                "__ef_migrations_history",
                "identity"));

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityServiceDbContext>()
                    .ReplaceDefaultEntities<Guid>();
            });
    }
}
