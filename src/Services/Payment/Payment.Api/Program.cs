using FluentValidation;
using MassTransit;
using MediatR;
using Microservices.Application;
using Microservices.Messaging;
using Microservices.Persistence.Postgres;
using Microservices.Security;
using Microservices.ServiceDefaults;
using Microservices.ServiceDefaults.ProblemDetails;
using Payment.Api.Features.PaymentMethods;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure.Stripe;
using Payment.Api.Integration;
using Payment.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddWebApiDefaults();
builder.AddApiDocumentation(
    "Payment API",
    new ApiDocumentationOAuthOptions(
        "payment-scalar-dev",
        "https://localhost:7070/scalar/v1",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid"] = "Authenticate the user.",
            ["profile"] = "Read the user's basic identity profile.",
            ["payment-api-audience"] = "Request a token for Payment API.",
            ["payment-api-roles"] = "Request Payment API client roles.",
            [PaymentAuthorization.ReadScope] = "Read saved payment methods.",
            [PaymentAuthorization.WriteScope] = "Manage saved payment methods."
        }));
builder.Services.AddMicroserviceProblemDetails();
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
builder.Services.AddPostgresDbContext<PaymentDbContext>(builder.Configuration, "payment-db");
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>(
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

builder.Services.AddOptions<StripeOptions>()
    .Bind(builder.Configuration.GetSection(StripeOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey), "Stripe SecretKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.WebhookSecret), "Stripe WebhookSecret is required.")
    .ValidateOnStart();
builder.Services.AddScoped<IStripeGateway, StripeGateway>();
builder.Services.AddHostedService<StripeWebhookProcessor>();

builder.Services.AddRabbitMqWithPostgresOutbox<PaymentDbContext>(
    builder.Configuration,
    "payment",
    configureRegistrations: registration =>
        registration.AddConsumer<CustomerIdentitySynchronizedConsumer>());

var app = builder.Build();

app.UseConfiguredForwardedHeaders();
app.UseMicroserviceProblemDetails();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapApiDocumentation();
app.MapMicroserviceErrorCatalog();
app.MapPaymentMethodEndpoints();
StripeWebhookEndpoint.Map(app);

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar/v1"))
        .ExcludeFromDescription()
        .AllowAnonymous();
}

await app.RunAsync();

public partial class Program;
