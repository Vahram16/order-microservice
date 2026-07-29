var builder = DistributedApplication.CreateBuilder(args);

const string keycloakBaseUrl = "https://localhost:8080";
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
var keycloakDatabase = postgres.AddDatabase("keycloak-db", "keycloak");
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume();

var keycloak = builder
    .AddKeycloak("keycloak", port: 8080)
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
    .WithLifetime(ContainerLifetime.Persistent)
    .WaitFor(keycloakDatabase);

var migrations = builder.AddProject<Projects.ServiceTemplate_Migrator>("service-template-migrator")
    .WithReference(serviceDatabase)
    .WaitFor(serviceDatabase);

builder.AddProject<Projects.ServiceTemplate_Api>(
        "service-template-api",
        launchProfileName: "https")
    .WithReference(serviceDatabase)
    .WithReference(rabbitMq)
    .WithReference(keycloak)
    .WithEnvironment("Security__Authority", keycloakIssuer)
    .WithEnvironment("Security__Audience", "order-api")
    .WithEnvironment("Security__RoleClientId", "order-api")
    .WithEnvironment("Security__ValidAuthorizedParties__0", "order-mobile")
    .WithEnvironment("Security__ValidAuthorizedParties__1", "scalar-dev")
    .WithEnvironment("Security__RequireHttpsMetadata", "true")
    .WithUrlForEndpoint("https", url =>
    {
        url.Url = "/scalar/v1";
        url.DisplayText = "Scalar API";
    })
    .WaitFor(serviceDatabase)
    .WaitForCompletion(migrations)
    .WaitFor(rabbitMq)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
