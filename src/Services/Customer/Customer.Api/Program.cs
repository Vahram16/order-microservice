using Customer.Api.Features.Customers;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Application;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApiDocumentation(
    "Customer API",
    new ApiDocumentationOAuthOptions(
        "customer-scalar-dev",
        "https://localhost:7050/scalar/v1",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid"] = "Authenticate the user.",
            ["profile"] = "Read the user's basic identity profile.",
            ["email"] = "Read the user's verified email address.",
            ["customer-api-audience"] = "Request a token for Customer API.",
            ["customer-api-roles"] = "Request Customer API client roles.",
            [CustomerAuthorization.ReadScope] = "Read the authenticated customer's data.",
            [CustomerAuthorization.UpdateScope] = "Provision and update the authenticated customer.",
            [CustomerAuthorization.AddressWriteScope] = "Manage the authenticated customer's saved addresses.",
            [CustomerAuthorization.ExportScope] = "Export Customer-service-owned personal data.",
            [CustomerAuthorization.DeleteScope] = "Close and anonymize the authenticated customer account."
        }));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CustomerExceptionHandler>();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<CustomerDbContext>(
    builder.Configuration,
    "customer-db");
builder.Services.AddHealthChecks().AddDbContextCheck<CustomerDbContext>();
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

var app = builder.Build();

app.UseConfiguredForwardedHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapApiDocumentation();
app.MapCustomerEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar/v1"))
        .ExcludeFromDescription()
        .AllowAnonymous();
}

await app.RunAsync();

public partial class Program;
