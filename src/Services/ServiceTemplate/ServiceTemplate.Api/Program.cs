using FluentValidation;
using MediatR;
using Microservices.Application;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using ServiceTemplate.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApiDocumentation("Service Template API");
builder.Services.AddProblemDetails();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<ServiceTemplateDbContext>(
    builder.Configuration,
    "service-template-db");
builder.Services.AddHealthChecks().AddDbContextCheck<ServiceTemplateDbContext>();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<Program>();
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
    configuration.LicenseKey = builder.Configuration["Licensing:MediatR"];
});
builder.Services.AddRabbitMqWithPostgresOutbox<ServiceTemplateDbContext>(
    builder.Configuration,
    "service-template");

var app = builder.Build();

app.UseConfiguredForwardedHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapApiDocumentation();

await app.RunAsync();

public partial class Program;