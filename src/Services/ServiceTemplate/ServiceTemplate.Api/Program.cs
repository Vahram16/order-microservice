using FluentValidation;
using MediatR;
using Microservices.Application;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using ServiceTemplate.Api.Persistence;
using System.Security.Claims;

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
app.MapGet("/", () => Results.Redirect("/scalar/v1"))
    .ExcludeFromDescription()
    .AllowAnonymous();
app.MapGet("/auth-test", (ClaimsPrincipal user, ILogger<Program> logger) =>
    {
        var userId = user.FindFirstValue("sub");
        var username = user.FindFirstValue("preferred_username") ?? user.Identity?.Name;

        Program.LogAuthTestRequest(logger, username, userId);

        return Results.Ok(new
        {
            message = "Authentication works.",
            userId,
            username
        });
    })
    .WithName("AuthTest")
    .WithSummary("Tests Keycloak access-token authentication")
    .RequireAuthorization( RolePolicy.For("order.read"));

await app.RunAsync();

public partial class Program
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Auth test request received from user {Username} ({UserId})")]
    public static partial void LogAuthTestRequest(
        ILogger logger,
        string? username,
        string? userId);
}
