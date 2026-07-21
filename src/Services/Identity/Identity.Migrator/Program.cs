using Identity.Api.Infrastructure;
using Identity.Api.Persistence;
using Identity.Api.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.AddServiceDefaults();
builder.AddIdentityPersistence();
builder.Services.AddScoped<AuthorizationServerProvisioner>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<IdentityServiceDbContext>();
await dbContext.Database.MigrateAsync();

var provisioner = scope.ServiceProvider.GetRequiredService<AuthorizationServerProvisioner>();
await provisioner.ProvisionAsync();
