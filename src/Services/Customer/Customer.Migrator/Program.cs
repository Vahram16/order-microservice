using Customer.Api.Persistence;
using Microservices.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddPostgresDbContext<CustomerDbContext>(
    builder.Configuration,
    "customer-db");

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
await dbContext.Database.MigrateAsync();
