var builder = DistributedApplication.CreateBuilder(args);

const string keycloakBaseUrl = "http://localhost:8080";
const string keycloakIssuer = $"{keycloakBaseUrl}/realms/order";

var postgresUser = builder.AddParameter("postgres-user", "postgres", publishValueAsDefault: true);
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

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

var keycloak = builder
    .AddKeycloak("keycloak", port: 8080)
    .WithImageTag("26.7.0")
    .WithDataVolume("order-keycloak-data")
    .WithRealmImport("Keycloak")
    .WithEnvironment("KC_HOSTNAME", keycloakBaseUrl)
    .WithEnvironment("KC_HOSTNAME_STRICT", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WithLifetime(ContainerLifetime.Persistent);

var migrations = builder.AddProject<Projects.ServiceTemplate_Migrator>("service-template-migrator")
    .WithReference(serviceDatabase)
    .WaitFor(serviceDatabase);

builder.AddProject<Projects.ServiceTemplate_Api>("service-template-api")
    .WithReference(serviceDatabase)
    .WithReference(rabbitMq)
    .WithReference(keycloak)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "order-api")
    .WithEnvironment("Security__RoleClientId", "order-api")
    .WithEnvironment("Security__ValidAuthorizedParties__0", "order-mobile")
    .WithEnvironment("Security__RequireHttpsMetadata", "false")
    .WaitFor(serviceDatabase)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
