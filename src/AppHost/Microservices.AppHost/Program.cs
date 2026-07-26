var builder = DistributedApplication.CreateBuilder(args);

const string identityIssuer = "https://localhost:7100/";

var postgresUser = builder.AddParameter("postgres-user", "postgres", publishValueAsDefault: true);
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);
var notificationsIngressApiKey = builder.AddParameter(
    "notifications-ingress-api-key",
    "local-development-notifications-webhook-key-2026",
    secret: true);
var postmarkServerToken = builder.AddParameter(
    "postmark-server-token",
    "POSTMARK_API_TEST",
    secret: true);
var postmarkFromAddress = builder.AddParameter(
    "postmark-from-address",
    "notifications@example.com",
    publishValueAsDefault: true);

var postgres = builder
    .AddAzurePostgresFlexibleServer("postgres")
    .WithPasswordAuthentication(postgresUser, postgresPassword)
    .RunAsContainer(container =>
        container
            .WithImageTag("18")
            .WithHostPort(5432)
            .WithDataVolume("microservices-postgres-data")
            .WithLifetime(ContainerLifetime.Persistent));

var serviceDatabase = postgres.AddDatabase("service-template-db");
var identityDatabase = postgres.AddDatabase("identity-db");
var notificationsDatabase = postgres.AddDatabase("notifications-db");
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume();

var notificationsMigrations = builder
    .AddProject<Projects.Notifications_Migrator>("notifications-migrator")
    .WithReference(notificationsDatabase)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WaitFor(notificationsDatabase);

var notificationsApi = builder
    .AddProject<Projects.Notifications_Api>("notifications-api")
    .WithReference(notificationsDatabase)
    .WithEnvironment("NotificationsIngress__ApiKey", notificationsIngressApiKey)
    .WithEnvironment("Postmark__ServerToken", postmarkServerToken)
    .WithEnvironment("Postmark__FromAddress", postmarkFromAddress)
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WaitFor(notificationsDatabase)
    .WaitForCompletion(notificationsMigrations)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar";
        url.Url = "/scalar/v1";
    });

var notificationsIngressEndpoint = ReferenceExpression.Create(
    $"{notificationsApi.GetEndpoint("https")}/internal/v1/notifications/identity");

var identityMigrations = builder.AddProject<Projects.Identity_Migrator>("identity-migrator")
    .WithReference(identityDatabase)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WaitFor(identityDatabase);

var identityApi = builder.AddProject<Projects.Identity_Api>("identity-api")
    .WithReference(identityDatabase)
    .WithReference(notificationsApi)
    .WithEnvironment("AuthorizationServer__Issuer", identityIssuer)
    .WithEnvironment("IdentityNotifications__Provider", "Webhook")
    .WithEnvironment("IdentityNotifications__WebhookEndpoint", notificationsIngressEndpoint)
    .WithEnvironment("IdentityNotifications__WebhookApiKey", notificationsIngressApiKey)
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WaitFor(identityDatabase)
    .WaitForCompletion(identityMigrations)
    .WaitFor(notificationsApi)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar";
        url.Url = "/scalar/v1";
    });

var migrations = builder.AddProject<Projects.ServiceTemplate_Migrator>("service-template-migrator")
    .WithReference(serviceDatabase)
    .WaitFor(serviceDatabase);

builder.AddProject<Projects.ServiceTemplate_Api>("service-template-api")
    .WithReference(serviceDatabase)
    .WithReference(rabbitMq)
    .WithReference(identityApi)
    .WithEnvironment("Security__Authority", identityIssuer)
    .WithEnvironment("Security__Audience", "booking-public-api")
    .WaitFor(serviceDatabase)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitMq)
    .WaitFor(identityApi);

await builder.Build().RunAsync();
