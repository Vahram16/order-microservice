using FluentValidation;
using MassTransit;
using MediatR;
using Microservices.Application;
using Microservices.Contracts.Inventory.V1;
using Microservices.Contracts.Payments.V1;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using Microservices.ServiceDefaults;
using Microservices.ServiceDefaults.ProblemDetails;
using Order.Api;
using Order.Api.Features.Orders;
using Order.Api.Features.Orders.Common;
using Order.Api.Integration;
using Order.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebApiDefaults();
builder.AddApiDocumentation("Order API", new ApiDocumentationOAuthOptions("mobile-app", "https://localhost:7090/scalar/v1", new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["openid"] = "Authenticate the user.", ["profile"] = "Read the user's basic identity profile.", ["backend-api-audience"] = "Request a token for the backend API."
}));
builder.Services.AddMicroserviceProblemDetails(); builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<OrderDbContext>(builder.Configuration, "order-db");
builder.Services.AddHealthChecks().AddDbContextCheck<OrderDbContext>(tags: [ServiceHealthCheckTags.Readiness]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<OrderWorkflowOptions>().Bind(builder.Configuration.GetSection(OrderWorkflowOptions.SectionName)).Validate(options => options.CheckoutTimeout > TimeSpan.Zero, "CheckoutTimeout must be positive.").ValidateOnStart();
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Scoped, includeInternalTypes: true);
builder.Services.AddMediatR(configuration => { configuration.RegisterServicesFromAssemblyContaining<Program>(); configuration.AddOpenBehavior(typeof(ValidationBehavior<,>)); configuration.LicenseKey = builder.Configuration["Licensing:MediatR"]; });
builder.Services.AddRabbitMqWithPostgresOutbox<OrderDbContext>(builder.Configuration, "order", configureRegistrations: registration =>
{
    registration.AddConsumer<CustomerIdentitySynchronizedConsumer>(); registration.AddConsumer<ProductCatalogChangedConsumer>(); registration.AddConsumer<InventoryReservedConsumer>();
    registration.AddConsumer<InventoryRejectedConsumer>(); registration.AddConsumer<PaymentActionRequiredConsumer>(); registration.AddConsumer<PaymentAuthorizedConsumer>();
    registration.AddConsumer<PaymentRejectedConsumer>(); registration.AddConsumer<InventoryReservationCommittedConsumer>(); registration.AddConsumer<InventoryReservationExpiredConsumer>();
    registration.AddConsumer<PaymentCapturedConsumer>(); registration.AddConsumer<PaymentCaptureFailedConsumer>();
});
builder.Services.AddIntegrationCommandRoute<ReserveInventory>(ReserveInventory.EndpointName);
builder.Services.AddIntegrationCommandRoute<ReleaseInventory>(ReleaseInventory.EndpointName);
builder.Services.AddIntegrationCommandRoute<CommitInventoryReservation>(CommitInventoryReservation.EndpointName);
builder.Services.AddIntegrationCommandRoute<AuthorizeOrderPayment>(AuthorizeOrderPayment.EndpointName);
builder.Services.AddIntegrationCommandRoute<CaptureOrderPayment>(CaptureOrderPayment.EndpointName);
builder.Services.AddIntegrationCommandRoute<CancelOrderPayment>(CancelOrderPayment.EndpointName);
builder.Services.AddHostedService<OrderExpirationWorker>();

var app = builder.Build(); app.UseConfiguredForwardedHeaders(); app.UseMicroserviceProblemDetails(); app.UseHttpsRedirection(); app.UseAuthentication(); app.UseAuthorization();
app.MapDefaultEndpoints(); app.MapApiDocumentation(); OrderErrorCatalog.Map(app); app.MapOrderEndpoints();
if (app.Environment.IsDevelopment()) app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription().AllowAnonymous();
await app.RunAsync();
public partial class Program;
