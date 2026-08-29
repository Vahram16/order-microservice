using Customer.Api.Features.Customers;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Integration;
using Customer.Api.Persistence;
using FluentValidation;
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
    "Customer API",
    new ApiDocumentationOAuthOptions(
        "mobile-app",
        "https://localhost:7050/scalar/v1",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid"] = "Authenticate the user.",
            ["profile"] = "Read the user's basic identity profile.",
            ["email"] = "Read the user's verified email address.",
            ["backend-api-audience"] = "Request a token for the backend API."
        }));
builder.Services.AddMicroserviceProblemDetails();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<CustomerDbContext>(builder.Configuration, "customer-db");
builder.Services.AddHealthChecks().AddDbContextCheck<CustomerDbContext>(
    tags: [ServiceHealthCheckTags.Readiness]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>(
    ServiceLifetime.Scoped,
    includeInternalTypes: true);
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<Program>();
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
    configuration.LicenseKey = builder.Configuration["Licensing:MediatR"];
});
builder.Services.AddRabbitMqWithPostgresOutbox<CustomerDbContext>(
    builder.Configuration,
    "customer",
    configureRegistrations: registration =>
        registration.AddConsumer<
            CustomerIdentitySnapshotConsumer,
            CustomerIdentitySnapshotConsumerDefinition>());

var app = builder.Build();

app.UseConfiguredForwardedHeaders();
app.UseMicroserviceProblemDetails();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapApiDocumentation();
app.MapMicroserviceErrorCatalog();
CustomerErrorCatalog.Map(app);
app.MapCustomerEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar/v1"))
        .ExcludeFromDescription()
        .AllowAnonymous();
}

await app.RunAsync();

public partial class Program;
