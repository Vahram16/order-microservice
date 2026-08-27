using MassTransit;
using MediatR;
using Microservices.Contracts.Payments.V1;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using Microservices.ServiceDefaults;
using Microservices.ServiceDefaults.ProblemDetails;
using Payment.Api.Features.OrderPayments;
using Payment.Api.Features.PaymentMethods;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure.Stripe;
using Payment.Api.Integration;
using Payment.Api.Persistence;
using Payment.Api.Webhooks;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebApiDefaults();
builder.AddApiDocumentation("Payment API", new ApiDocumentationOAuthOptions("mobile-app", "https://localhost:7070/scalar/v1", new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["openid"] = "Authenticate the user.", ["profile"] = "Read the user's basic identity profile.", ["backend-api-audience"] = "Request a token for the backend API."
}));
builder.Services.AddMicroserviceProblemDetails();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<PaymentDbContext>(builder.Configuration, "payment-db");
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>(tags: [ServiceHealthCheckTags.Readiness]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMediatR(configuration => { configuration.RegisterServicesFromAssemblyContaining<Program>(); configuration.LicenseKey = builder.Configuration["Licensing:MediatR"]; });
builder.Services.AddStripePayments(builder.Configuration);
builder.Services.AddRabbitMqWithPostgresOutbox<PaymentDbContext>(builder.Configuration, "payment", configureRegistrations: registration =>
{
    registration.AddConsumer<CustomerIdentitySynchronizedConsumer>();
    registration.AddConsumer<ProcessStripeWebhookConsumer>();
    registration.AddConsumer<AuthorizeOrderConsumer>();
    registration.AddConsumer<CaptureOrderPaymentConsumer>();
    registration.AddConsumer<CancelOrderConsumer>();
}, useConsumerOutbox: endpointName =>
    !string.Equals(endpointName, ProcessStripeWebhook.EndpointName, StringComparison.Ordinal) &&
    !string.Equals(endpointName, AuthorizeOrderPayment.EndpointName, StringComparison.Ordinal) &&
    !string.Equals(endpointName, CaptureOrderPayment.EndpointName, StringComparison.Ordinal) &&
    !string.Equals(endpointName, CancelOrderPayment.EndpointName, StringComparison.Ordinal));
builder.Services.AddIntegrationCommandRoute<ProcessStripeWebhook>(ProcessStripeWebhook.EndpointName);

var app = builder.Build();
app.UseConfiguredForwardedHeaders(); app.UseMicroserviceProblemDetails(); app.UseHttpsRedirection(); app.UseAuthentication(); app.UseAuthorization();
app.MapDefaultEndpoints(); app.MapApiDocumentation(); PaymentErrorCatalog.Map(app); app.MapPaymentMethodEndpoints(); app.MapOrderPaymentEndpoints(); StripeWebhookEndpoint.Map(app);
if (app.Environment.IsDevelopment()) app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription().AllowAnonymous();
await app.RunAsync();
public partial class Program;
