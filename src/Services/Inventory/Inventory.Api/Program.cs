using FluentValidation;
using Inventory.Api.Features.Inventory;
using Inventory.Api.Integration;
using Inventory.Api.Persistence;
using MassTransit;
using MediatR;
using Microservices.Application;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using Microservices.ServiceDefaults;
using Microservices.ServiceDefaults.ProblemDetails;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebApiDefaults();
builder.AddApiDocumentation(
    "Inventory API",
    new ApiDocumentationOAuthOptions(
        "mobile-app",
        "https://localhost:7100/scalar/v1",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid"] = "Authenticate the user.",
            ["backend-api-audience"] = "Request a token for the backend API."
        }));
builder.Services.AddMicroserviceProblemDetails();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<InventoryDbContext>(builder.Configuration, "inventory-db");
builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>(tags: [ServiceHealthCheckTags.Readiness]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Scoped, includeInternalTypes: true);
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<Program>();
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
    configuration.LicenseKey = builder.Configuration["Licensing:MediatR"];
});
builder.Services.AddSingleton<IConsumerExceptionRule, InventoryPersistenceExceptionRule>();
builder.Services.AddRabbitMqWithPostgresOutbox<InventoryDbContext>(
    builder.Configuration,
    "inventory",
    configureRegistrations: registration =>
    {
        registration.AddConsumer<ReserveInventoryConsumer>();
        registration.AddConsumer<ReleaseInventoryConsumer>();
        registration.AddConsumer<CommitInventoryReservationConsumer>();
    });
builder.Services.AddHostedService<InventoryReservationExpirationWorker>();

var app = builder.Build();
app.UseConfiguredForwardedHeaders();
app.UseMicroserviceProblemDetails();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapApiDocumentation();
app.MapInventoryEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription().AllowAnonymous();
}
await app.RunAsync();

public partial class Program;
