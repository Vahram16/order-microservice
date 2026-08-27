using FluentValidation;
using MassTransit;
using MediatR;
using Microservices.Application;
using Microservices.Contracts.Products.V1;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using Microservices.ServiceDefaults;
using Microservices.ServiceDefaults.ProblemDetails;
using Product.Api.Features.Products;
using Product.Api.Features.Products.Common;
using Product.Api.Integration;
using Product.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebApiDefaults();
builder.AddApiDocumentation("Product API", new ApiDocumentationOAuthOptions("mobile-app", "https://localhost:7060/scalar/v1", new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["openid"] = "Authenticate the user.", ["backend-api-audience"] = "Request a token for the backend API."
}));
builder.Services.AddMicroserviceProblemDetails(); builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<ProductDbContext>(builder.Configuration, "product-db");
builder.Services.AddHealthChecks().AddDbContextCheck<ProductDbContext>(tags: [ServiceHealthCheckTags.Readiness]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Scoped, includeInternalTypes: true);
builder.Services.AddMediatR(configuration => { configuration.RegisterServicesFromAssemblyContaining<Program>(); configuration.AddOpenBehavior(typeof(ValidationBehavior<,>)); configuration.LicenseKey = builder.Configuration["Licensing:MediatR"]; });
builder.Services.AddRabbitMqWithPostgresOutbox<ProductDbContext>(builder.Configuration, "product", configureRegistrations: registration => registration.AddConsumer<ProductCatalogSnapshotConsumer>());
builder.Services.AddIntegrationCommandRoute<SynchronizeProductCatalogSnapshot>(SynchronizeProductCatalogSnapshot.EndpointName);

var app = builder.Build(); app.UseConfiguredForwardedHeaders(); app.UseMicroserviceProblemDetails(); app.UseHttpsRedirection(); app.UseAuthentication(); app.UseAuthorization();
app.MapDefaultEndpoints(); app.MapApiDocumentation(); app.MapMicroserviceErrorCatalog(); ProductErrorCatalog.Map(app); app.MapProductEndpoints();
if (app.Environment.IsDevelopment()) app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription().AllowAnonymous();
await app.RunAsync();
public partial class Program;
