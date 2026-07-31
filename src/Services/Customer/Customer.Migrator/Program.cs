using Customer.Api.Persistence;
using Microservices.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddJobDefaults();
builder.Services.AddPostgresDbContext<CustomerDbContext>(
    builder.Configuration,
    "customer-db");

using var host = builder.Build();
await host.StartAsync();

try
{
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await using var scope = host.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
    await dbContext.Database.MigrateAsync(lifetime.ApplicationStopping);
}
finally
{
    await host.StopAsync();
}
