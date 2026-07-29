using Customer.Api.Features;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Application;
using Microservices.Persistence.Postgres;
using Microservices.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApiDocumentation("Customer API");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CustomerExceptionHandler>();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<CustomerDbContext>(
    builder.Configuration,
    "customer-db");
builder.Services.AddHealthChecks().AddDbContextCheck<CustomerDbContext>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
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
app.MapGet("/", () => Results.Redirect("/scalar/v1"))
    .ExcludeFromDescription()
    .AllowAnonymous();

await app.RunAsync();

public partial class Program;
