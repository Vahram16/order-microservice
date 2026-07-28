var builder = DistributedApplication.CreateBuilder(args);

const string keycloakIssuer = "http://localhost:8080/realms/order";

var postgresUser = builder.AddParameter("postgres-user", "postgres", publishValueAsDefault: true);
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);
var keycloakAdminUser = builder.AddParameter(
    "keycloak-admin-user",
    "admin",
    publishValueAsDefault: true);
var keycloakAdminPassword = builder.AddParameter(
    "keycloak-admin-password",
    secret: true);

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
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume();

var keycloakImportDirectory = Path.Combine(AppContext.BaseDirectory, "Keycloak");
var keycloak = builder
    .AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.7.0")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", keycloakAdminUser)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WithEnvironment("KC_HOSTNAME", "http://localhost:8080")
    .WithEnvironment("KC_HOSTNAME_STRICT", "true")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "management")
    .WithBindMount(
        keycloakImportDirectory,
        "/opt/keycloak/data/import",
        isReadOnly: true)
    .WithVolume("order-keycloak-data", "/opt/keycloak/data")
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Keycloak";
        url.Url = "/admin/master/console/";
    });

var migrations = builder.AddProject<Projects.ServiceTemplate_Migrator>("service-template-migrator")
    .WithReference(serviceDatabase)
    .WaitFor(serviceDatabase);

builder.AddProject<Projects.ServiceTemplate_Api>("service-template-api")
    .WithReference(serviceDatabase)
    .WithReference(rabbitMq)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "order-api")
    .WithEnvironment("Security__RoleClientId", "order-api")
    .WithEnvironment("Security__RequireHttpsMetadata", "false")
    .WaitFor(serviceDatabase)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
