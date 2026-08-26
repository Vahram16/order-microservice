var builder = DistributedApplication.CreateBuilder(args);

const string keycloakBaseUrl = "https://localhost:8080";
const string keycloakIssuer = $"{keycloakBaseUrl}/realms/order";

var postgresUser = builder.AddParameter("postgres-user", "postgres", publishValueAsDefault: true);
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);
var keycloakPassword = builder.AddParameter("keycloak-password", secret: true);
var rabbitMqUser = builder.AddParameter("rabbitmq-user", "guest", publishValueAsDefault: true);
var rabbitMqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);
var stripeSecretKey = builder.AddParameter("stripe-secret-key", secret: true);
var stripeWebhookSecret = builder.AddParameter("stripe-webhook-secret", secret: true);

var postgres = builder
    .AddAzurePostgresFlexibleServer("postgres")
    .WithPasswordAuthentication(postgresUser, postgresPassword)
    .RunAsContainer(container =>
        container
            .WithImageTag("18")
            .WithHostPort(5432)
            .WithDataVolume("microservices-postgres-data"));

var serviceDatabase = postgres.AddDatabase("service-template-db");
var customerDatabase = postgres.AddDatabase("customer-db", "customer");
var paymentDatabase = postgres.AddDatabase("payment-db", "payment");
var productDatabase = postgres.AddDatabase("product-db", "product");
var keycloakDatabase = postgres.AddDatabase("keycloak-db", "keycloak");
var rabbitMq = builder.AddRabbitMQ("rabbitmq", rabbitMqUser, rabbitMqPassword)
    .WithManagementPlugin()
    .WithDockerfile("../../../infrastructure/rabbitmq", "Containerfile")
    .WithHttpEndpoint(targetPort: 15692, name: "prometheus")
    .WithDataVolume();

var keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithHttpsEndpoint(port: 8080, targetPort: 8443, name: "public", isProxied: false)
    .WithImageTag("26.7.0")
    .WithRealmImport("Keycloak")
    .WithReference(keycloakDatabase)
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL", keycloakDatabase.Resource.JdbcConnectionString)
    .WithEnvironment("KC_DB_USERNAME", postgresUser)
    .WithEnvironment("KC_DB_PASSWORD", postgresPassword)
    .WithEnvironment("KC_HOSTNAME", keycloakBaseUrl)
    .WithEnvironment("KC_HOSTNAME_STRICT", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WaitFor(keycloakDatabase);

var serviceMigrations = builder.AddProject<Projects.ServiceTemplate_Migrator>("service-template-migrator")
    .WithReference(serviceDatabase)
    .WaitFor(serviceDatabase);

builder.AddProject<Projects.ServiceTemplate_Api>("service-template-api", launchProfileName: "https")
    .WithReference(serviceDatabase)
    .WithReference(rabbitMq)
    .WithReference(keycloak)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "backend-api")
    .WithEnvironment("Security__RoleClientId", "backend-api")
    .WithEnvironment("Security__ValidAuthorizedParties__0", "mobile-app")
    .WithEnvironment("Security__RequireHttpsMetadata", "true")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithUrlForEndpoint("https", url => { url.Url = "/scalar/v1"; url.DisplayText = "Scalar API"; })
    .WaitFor(serviceDatabase)
    .WaitForCompletion(serviceMigrations)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak);

var customerMigrations = builder.AddProject<Projects.Customer_Migrator>("customer-migrator")
    .WithReference(customerDatabase)
    .WaitFor(customerDatabase);

builder.AddProject<Projects.Customer_Api>("customer-api", launchProfileName: "https")
    .WithReference(customerDatabase)
    .WithReference(rabbitMq)
    .WithReference(keycloak)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "backend-api")
    .WithEnvironment("Security__RoleClientId", "backend-api")
    .WithEnvironment("Security__ValidAuthorizedParties__0", "mobile-app")
    .WithEnvironment("Security__RequireHttpsMetadata", "true")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithUrlForEndpoint("https", url => { url.Url = "/scalar/v1"; url.DisplayText = "Customer Scalar API"; })
    .WaitFor(customerDatabase)
    .WaitForCompletion(customerMigrations)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak);

var paymentMigrations = builder.AddProject<Projects.Payment_Migrator>("payment-migrator")
    .WithReference(paymentDatabase)
    .WaitFor(paymentDatabase);

builder.AddProject<Projects.Payment_Api>("payment-api", launchProfileName: "https")
    .WithReference(paymentDatabase)
    .WithReference(rabbitMq)
    .WithReference(keycloak)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "backend-api")
    .WithEnvironment("Security__RoleClientId", "backend-api")
    .WithEnvironment("Security__ValidAuthorizedParties__0", "mobile-app")
    .WithEnvironment("Security__RequireHttpsMetadata", "true")
    .WithEnvironment("Stripe__SecretKey", stripeSecretKey)
    .WithEnvironment("Stripe__WebhookSecret", stripeWebhookSecret)
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithUrlForEndpoint("https", url => { url.Url = "/scalar/v1"; url.DisplayText = "Payment Scalar API"; })
    .WaitFor(paymentDatabase)
    .WaitForCompletion(paymentMigrations)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak);

var productMigrations = builder.AddProject<Projects.Product_Migrator>("product-migrator")
    .WithReference(productDatabase)
    .WaitFor(productDatabase);

builder.AddProject<Projects.Product_Api>("product-api", launchProfileName: "https")
    .WithReference(productDatabase)
    .WithReference(keycloak)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "backend-api")
    .WithEnvironment("Security__RoleClientId", "backend-api")
    .WithEnvironment("Security__ValidAuthorizedParties__0", "mobile-app")
    .WithEnvironment("Security__RequireHttpsMetadata", "true")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithUrlForEndpoint("https", url => { url.Url = "/scalar/v1"; url.DisplayText = "Product Scalar API"; })
    .WaitFor(productDatabase)
    .WaitForCompletion(productMigrations)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
