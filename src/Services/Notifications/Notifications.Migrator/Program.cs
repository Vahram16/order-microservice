using Microservices.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Api.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddPostgresDbContext<NotificationDbContext>(
    builder.Configuration,
    "notifications-db",
    _ => { },
    postgres => postgres.MigrationsHistoryTable(
        "__ef_migrations_history",
        "notifications"));

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
await dbContext.Database.MigrateAsync();
