var builder = DistributedApplication.CreateBuilder(args);

const string identityIssuer = "https://localhost:7100/";

var postgres = builder.AddPostgres("postgres").WithDataVolume();
var serviceDatabase = postgres.AddDatabase("service-template-db");
var identityDatabase = postgres.AddDatabase("identity-db");
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume();

var identityMigrations = builder.AddProject<Projects.Identity_Migrator>("identity-migrator")
    .WithReference(identityDatabase)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WaitFor(identityDatabase);

var identityApi = builder.AddProject<Projects.Identity_Api>("identity-api")
    .WithReference(identityDatabase)
    .WithEnvironment("AuthorizationServer__Issuer", identityIssuer)
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WaitFor(identityDatabase)
    .WaitForCompletion(identityMigrations)
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
    .WaitFor(identityApi)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar";
        url.Url = "/scalar/v1";
    });

await builder.Build().RunAsync();
