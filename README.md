# Microservices Boilerplate

A .NET 10 foundation for independently deployable services with PostgreSQL, RabbitMQ, transactional
outbox/inbox support, OpenTelemetry, health checks, OpenAPI, and Keycloak-backed authentication.

The repository contains infrastructure plumbing and a service template. It intentionally does not
invent an Order domain, aggregates, endpoints, or authorization rules that do not yet exist.

## Architecture

```text
Native mobile application
    |
    | OpenID Connect Authorization Code + PKCE (S256)
    v
Keycloak
    |
    | signed access token
    v
Resource API
    |
    | JWT validation, scopes, roles, and future domain authorization
    v
Application and domain
```

```text
src/
  AppHost/
    Microservices.AppHost/          local Aspire orchestration and development realm import
  Services/
    ServiceTemplate/
      ServiceTemplate.Api/          resource API composition root and vertical-slice template
      ServiceTemplate.Migrator/     run-once schema migration
  Shared/
    Microservices.Application/      CQRS contracts, validation, and approved publish boundary
    Microservices.Contracts/        framework-free integration contracts
    Microservices.Messaging/        MassTransit/RabbitMQ and EF inbox/outbox
    Microservices.Persistence.Postgres/
    Microservices.Security/         JWT validation and authorization policies
    Microservices.ServiceDefaults/  observability, resilience, health, and OpenAPI
tests/
  Microservices.ArchitectureTests/
  Microservices.Messaging.Tests/
  Microservices.Security.Tests/
infrastructure/
  keycloak/                         optimized production identity image definition
  rabbitmq/                         pinned broker image with delayed exchange and Prometheus
  observability/                    messaging dashboard, scrape config, and alert rules
```

The former in-repository Identity/OpenIddict service, migrator, database, tests, and packages have
been removed. Keycloak is the identity provider. Application services are OAuth resource servers.

## Responsibility boundary

Keycloak owns:

- users, credentials, password policy, recovery, MFA/passkeys, and federation;
- browser login/logout sessions;
- OAuth 2.0/OpenID Connect endpoints;
- access, refresh, and ID token issuance;
- signing keys and identity administration.

Each resource API owns:

- issuer, audience, signature, lifetime, token-type, and authorized-party validation;
- scope and application-role enforcement;
- tenant, resource-ownership, state-transition, and other domain authorization once a domain exists.

An API never receives a user's Keycloak password and never exchanges a mobile user's authorization
code.

## Mobile client

The development realm defines `order-mobile` as a public native client:

- Authorization Code Flow enabled;
- PKCE `S256` required;
- Direct Access Grants/password grant disabled;
- Implicit Flow disabled;
- Service Accounts disabled;
- no client secret;
- Full Scope Allowed disabled;
- exact development redirect URI `com.example.order:/oauth2redirect`.

Only the `order-user` API client role is in the mobile client's role scope mapping. The
`order-manager` and `order-admin` roles are not exposed through that public client.

Replace the development redirect URI with an exact Android App Link, iOS Universal Link, or an
application-owned custom scheme before release. The mobile application must open the system browser,
complete the authorization flow, exchange the code with its PKCE verifier, and attach only the
access token as a bearer token to API calls.

## Local API documentation client

The development realm also defines `scalar-dev`, a public PKCE client for Scalar at
`https://localhost:7040/scalar/v1`. It has the same explicitly granted API audience, application
scopes, and `order-user` role boundary as the mobile client, but it does not request
`offline_access` because interactive API documentation does not need an offline refresh token. It
is accepted only by the development API configuration; production continues to allow only
explicitly configured production clients.

The imported realm explicitly assigns Keycloak's `basic` client scope to both public clients so
their access tokens contain the stable OIDC `sub` identifier required by the API.

Run the API with its HTTPS launch profile when using Scalar OAuth. The exact redirect URI and web
origin are intentionally not wildcards.

## API token validation

Every resource API calls:

```csharp
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
```

Example production configuration:

```json
{
  "Security": {
    "Authority": "https://identity.example.com/realms/order",
    "Audience": "order-api",
    "RoleClientId": "order-api",
    "ValidAuthorizedParties": [ "order-mobile" ],
    "RequiredClaims": [ "sub", "iat", "jti" ],
    "MapRealmRoles": false,
    "NameClaimType": "preferred_username",
    "RequireHttpsMetadata": true,
    "ClockSkew": "00:00:30",
    "ValidTokenTypes": [ "JWT", "at+jwt" ]
  }
}
```

Validation uses Keycloak's OIDC discovery document and JWKS. Inbound Microsoft claim mapping is
disabled. The bearer handler validates issuer, audience, signature, expiration, signing key, and JWT
type. Post-validation checks require the configured claims and an exact `azp` match against
`ValidAuthorizedParties`. This prevents a token issued to an unapproved client from being accepted
merely because it also contains the API audience.

Authentication is the fallback authorization policy, so an endpoint is public only when it
explicitly calls `AllowAnonymous()`.

Keycloak client roles are flattened only from
`resource_access.<RoleClientId>.roles` into the application's role claim. Roles for other clients are
ignored. Realm roles are disabled by default and must be explicitly enabled.

## Scope and role policies

The shared library provides dynamic policies for future domain endpoints:

```csharp
endpoint.RequireAuthorization(ScopePolicy.For("orders.read"));
adminGroup.RequireAuthorization(RolePolicy.For("order-admin"));
```

These are infrastructure examples, not existing Order endpoints. The repository currently contains
`ServiceTemplate.Api`, not an Order service. When a real domain is added, its handlers must implement
its actual subject, tenant, resource-ownership, and state-transition rules. Scopes and roles never
replace those checks.

## Messaging reliability

Every service using `AddRabbitMqWithPostgresOutbox<TDbContext>` receives a pragmatic production
baseline:

- PostgreSQL bus outbox and consumer inbox/outbox;
- bounded, default-deny immediate retry and broker-backed delayed redelivery;
- a thin application-owned `IIntegrationMessagePublisher` backed by scoped `IPublishEndpoint`;
- framework-free integration contracts with explicit serializer compatibility rules;
- durable quorum business queues with count and byte limits, `reject-publish` overflow, and no
  receive-queue TTL;
- independently retained MassTransit `_error` and `_skipped` queues;
- lightweight consumer and outbox metrics;
- bounded startup and graceful shutdown;
- architecture tests that prohibit application and domain transport leakage;
- real RabbitMQ/PostgreSQL tests for externally meaningful reliability behavior.

MassTransit owns normal consume-context propagation and standard transport behavior. Services use
explicit stable endpoint names and `ConsumerDefinition<TConsumer>` when a consumer needs behavior
that differs materially from the shared defaults. Dependency-specific retry safety is expressed by
service-owned `IConsumerExceptionRule` implementations rather than a global exception catalogue.

See `docs/messaging-failure-delivery-policy.md` for the enforced baseline and `docs/adr/` for the
architectural decisions. Deployment-specific dashboards, alerts, indexes, and operational procedures
should be introduced when a real service workload and SLO justify them.

## Run locally with Aspire

Requirements:

- .NET 10 SDK;
- Docker Desktop, Podman, or another Aspire-supported container runtime;
- ports `5432` and `8080` available on the host.

Start the complete development environment:

```bash
dotnet tool restore
dotnet restore
dotnet run --project src/AppHost/Microservices.AppHost
```

Aspire starts:

- PostgreSQL on host port `5432`, with separate application and Keycloak databases;
- the pinned RabbitMQ image with the delayed-message exchange and Prometheus plugins;
- Keycloak 26.7.0 on host port `8080`;
- `ServiceTemplate.Migrator`;
- `ServiceTemplate.Api` after its dependencies are ready.

Open the Aspire dashboard URL printed by `dotnet run`. The Keycloak resource exposes its endpoint and
admin-console link there.

Development Keycloak endpoints:

```text
Issuer:       http://localhost:8080/realms/order
Admin console: http://localhost:8080/admin/master/console/
```

The official Aspire Keycloak hosting integration generates a random admin password on the first run
and stores it in the AppHost user-secrets store. The default admin username is `admin`. To inspect the
stored development credentials:

```bash
dotnet user-secrets list --project src/AppHost/Microservices.AppHost
```

Do not commit the generated password. The management endpoint is intentionally an internal Aspire
resource endpoint; use the Aspire dashboard to inspect its assigned URL and health status.

The realm import comes from:

```text
src/AppHost/Microservices.AppHost/Keycloak/order-realm.json
```

Keycloak stores its local state in the dedicated `keycloak` database on the Aspire-managed
PostgreSQL server; application data remains in a separate database. Startup import creates a missing
realm but does not reconcile an already existing realm. After intentionally changing the
development realm, delete the `order` realm through the Keycloak admin console and restart Aspire
so the startup import can recreate it.

The Aspire Keycloak package is used only by the local AppHost. Production does not deploy the
AppHost or depend on the Aspire integration package.

## Verification

The CI pipeline:

- restores, builds, and tests every .NET project with analyzers enforced;
- builds and starts the pinned RabbitMQ image with delayed redelivery and Prometheus support;
- uses real RabbitMQ and PostgreSQL to verify retry, delayed redelivery, `_error` and `_skipped`
  routing, duplicate suppression, outbox commit/rollback and recovery, broker/database recovery,
  queue topology, and graceful shutdown;
- scans production assemblies for prohibited bus, broker, persistence, contract, and test-helper
  dependencies;
- validates integration-contract serialization compatibility and stable endpoint configuration;
- validates the Grafana dashboard JSON and Prometheus scrape/rule files with `promtool`;
- retains build, test, PostgreSQL, RabbitMQ, plugin, and topology diagnostics when reliability tests
  fail;
- builds `infrastructure/keycloak/Containerfile`;
- starts the pinned Keycloak image with the development realm import;
- checks OIDC discovery and PKCE support;
- verifies the mobile and API client security flags through the Keycloak Admin API;
- verifies the mobile and Scalar client security flags and API-role scope mappings.

Run the Keycloak realm smoke test locally when Docker, `curl`, and `jq` are available:

```bash
bash scripts/verify-keycloak-development.sh
```

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
apply schema migrations during startup. Add workload-specific outbox monitoring indexes only after
representative query plans and production requirements justify them.

## Production Keycloak boundary

`infrastructure/keycloak/Containerfile` creates an optimized image pinned to Keycloak 26.7.0.
Production still requires environment-specific deployment and identity configuration, including:

- external PostgreSQL and tested backup/restore;
- HTTPS, stable public hostname, and trusted reverse-proxy configuration;
- secret-managed database and bootstrap credentials;
- restricted admin and management access;
- monitoring, alerting, capacity limits, and upgrade procedures;
- controlled realm/client reconciliation;
- production redirect URIs, SMTP, onboarding, MFA/step-up, and recovery policies.

Do not run `start-dev`, H2, wildcard redirect URIs, committed bootstrap credentials, or development
startup imports as the production configuration-management strategy. Reconcile realms and clients
through the Keycloak Operator or a controlled Admin API/GitOps process.

See `docs/keycloak-integration.md` for the detailed security contract.
