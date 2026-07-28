# Microservices Boilerplate

A .NET 10 foundation for independently deployable services with PostgreSQL, RabbitMQ, transactional
outbox/inbox support, OpenTelemetry, health checks, OpenAPI, and Keycloak-backed authentication.

The repository contains infrastructure plumbing only. It intentionally does not include fake order
aggregates or placeholder sagas.

## Architecture

```text
Mobile application
    |
    | OpenID Connect Authorization Code + PKCE (S256)
    v
Keycloak
    |
    | signed access token
    v
Resource API
    |
    | JWT validation, scopes, roles, tenant/ownership/domain rules
    v
Application and domain
```

```text
src/
  AppHost/
    Microservices.AppHost/          local Aspire orchestration and development realm import
  Services/
    ServiceTemplate/
      ServiceTemplate.Api/          resource API composition root and vertical slices
      ServiceTemplate.Migrator/     run-once schema migration
  Shared/
    Microservices.Application/      CQRS contracts and validation pipeline
    Microservices.Contracts/        framework-free integration contracts
    Microservices.Messaging/        MassTransit/RabbitMQ and EF inbox/outbox
    Microservices.Persistence.Postgres/
    Microservices.Security/         JWT validation and authorization policies
    Microservices.ServiceDefaults/  observability, resilience, health, OpenAPI
tests/
  Microservices.ArchitectureTests/
  Microservices.Messaging.Tests/
  Microservices.Security.Tests/
infrastructure/
  keycloak/                         optimized production image definition
```

The former in-repository Identity/OpenIddict service, migrator, database, tests, and packages have
been removed. Keycloak is the identity provider. Application services are OAuth resource servers.

## Authentication boundary

Keycloak owns:

- users, credentials, password policy, recovery, MFA/passkeys, and federation;
- browser login/logout sessions;
- OAuth 2.0/OpenID Connect endpoints;
- access/refresh/ID token issuance;
- signing keys and identity administration.

The API owns:

- issuer, audience, signature, lifetime, and token-type validation;
- scope and role enforcement;
- tenant and resource-ownership checks;
- state-transition and other domain authorization.

The API never receives a user's Keycloak password.

## Mobile client

The development realm defines `order-mobile` as a public native client:

- Standard Authorization Code Flow enabled;
- PKCE `S256` required;
- Direct Access Grants/password grant disabled;
- Implicit Flow disabled;
- Service Accounts disabled;
- no client secret;
- exact redirect URI: `com.example.order:/oauth2redirect`.

Replace that redirect URI with the exact Android App Link, iOS Universal Link, or application-owned
custom scheme before release. The mobile application must open the system browser, complete the
authorization flow, exchange the code with its PKCE verifier, and attach the access token as a bearer
token to API calls.

## API token validation

Every resource API calls:

```csharp
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
```

Production configuration:

```json
{
  "Security": {
    "Authority": "https://identity.example.com/realms/order",
    "Audience": "order-api",
    "RoleClientId": "order-api",
    "MapRealmRoles": false,
    "NameClaimType": "preferred_username",
    "RequireHttpsMetadata": true,
    "ClockSkew": "00:00:30",
    "ValidTokenTypes": [ "JWT", "at+jwt" ]
  }
}
```

Validation is performed from Keycloak's OIDC discovery document and JWKS. Inbound Microsoft claim
mapping is disabled. The API validates issuer, audience, signature, expiration, signing key, and
token type. Authentication is the fallback authorization policy, so an endpoint is public only when
it explicitly calls `AllowAnonymous()`.

Keycloak client roles are flattened from `resource_access.order-api.roles` into the application's
role claim. Roles for other clients are ignored. Realm roles are disabled by default and must be
explicitly enabled.

## Scope and role policies

Capability scopes are checked at the HTTP boundary:

```csharp
orders.MapGet("/{id:guid}", GetOrder)
    .RequireAuthorization(ScopePolicy.For("orders.read"));

orders.MapPost("/", CreateOrder)
    .RequireAuthorization(ScopePolicy.For("orders.create"));

orders.MapPost("/{id:guid}/cancel", CancelOrder)
    .RequireAuthorization(ScopePolicy.For("orders.cancel"));
```

Privileged groups can require a Keycloak client role:

```csharp
adminOrders.RequireAuthorization(RolePolicy.For("order-admin"));
```

Scopes and roles are not resource authorization. Handlers must still check the authenticated
subject, tenant, order ownership, order state, and any other invariant owned by the application.

## Development realm

The Aspire AppHost runs Keycloak `26.7.0` with a local development realm:

- issuer: `http://localhost:8080/realms/order`
- admin console: `http://localhost:8080/admin/master/console/`
- management readiness: `http://localhost:9000/health/ready`

The admin username and password are Aspire parameters; the password has no committed default. The
realm import contains clients, scopes, audience mapping, and API client roles, but no users or
passwords.

Keycloak's startup import skips a realm that already exists. Delete the local
`order-keycloak-data` volume when intentionally reapplying a changed development realm.

## Run locally

Requirements: .NET 10 SDK and an Aspire-supported container runtime.

```bash
dotnet tool restore
dotnet restore
dotnet run --project src/AppHost/Microservices.AppHost
```

Provide the Keycloak admin password through AppHost user secrets or the Aspire parameter prompt.

## Database migrations

Create a service-owned migration:

```bash
dotnet ef migrations add <Name> \
  --project src/Services/ServiceTemplate/ServiceTemplate.Api \
  --startup-project src/Services/ServiceTemplate/ServiceTemplate.Api \
  --context ServiceTemplateDbContext \
  --output-dir Persistence/Migrations
```

Deploy `ServiceTemplate.Migrator` as a run-once task before API replicas. API processes must not
apply schema migrations during startup.

## Production Keycloak

`infrastructure/keycloak/Containerfile` creates an optimized image pinned to Keycloak `26.7.0`.
Production must use PostgreSQL, HTTPS or a correctly configured trusted reverse proxy, secret-managed
database/admin credentials, restricted management endpoints, backups, monitoring, and tested
signing-key rotation.

Do not run `start-dev`, H2, wildcard redirect URIs, committed bootstrap credentials, or development
startup realm imports in production. Reconcile realms and clients through the Keycloak Operator or a
controlled Admin API/GitOps process.

See [docs/keycloak-integration.md](docs/keycloak-integration.md) for the detailed security contract.
