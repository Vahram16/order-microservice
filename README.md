# Microservices Boilerplate

An intentionally small .NET 10 foundation for independently deployable services.
It contains infrastructure plumbing only—no sample orders, fake aggregates, or placeholder saga.

## Included

- Vertical-slice-ready ASP.NET Core service template
- CQRS request abstractions and validation pipeline using MediatR
- PostgreSQL with one owned `DbContext` and database per service
- RabbitMQ through MassTransit
- MassTransit EF transactional bus outbox and consumer inbox/outbox
- Dedicated migration executable; API replicas never migrate on startup
- .NET Aspire local orchestration for PostgreSQL, RabbitMQ, migrations, and the API
- ASP.NET Core Identity and OpenIddict authorization server with production-safe defaults
- Shared JWT bearer validation and scope-based authorization for resource APIs
- Durable, encrypted account-notification outbox with retry, deduplication, and dead-lettering
- Multi-replica-safe OpenIddict token/authorization pruning and protocol rate limits
- OpenTelemetry logs, metrics, and traces
- Liveness and readiness endpoints
- Central package management, analyzers, warnings as errors, focused unit tests, and architecture tests

## Boundaries

```text
src/
  AppHost/                         local development orchestration
  Shared/
    Microservices.Application/     CQRS contracts and application pipelines
    Microservices.Contracts/       framework-free integration primitives only
    Microservices.Messaging/       MassTransit, RabbitMQ, and PostgreSQL EF inbox/outbox registration
    Microservices.Persistence.Postgres/ PostgreSQL registration only
    Microservices.Security/        JWT access-token validation and scope policies only
    Microservices.ServiceDefaults/ observability, resilience, discovery, health
  Services/
    Identity/
      Identity.Api/                users, account slices, OAuth 2.0/OpenID Connect, owned DbContext
      Identity.Migrator/           run-once schema migration and client/scope provisioning
    ServiceTemplate/
      ServiceTemplate.Api/         future vertical slices and owned DbContext
      ServiceTemplate.Migrator/    explicit schema deployment task
tests/
  Identity.Api.Tests/
  Microservices.ArchitectureTests/
  Microservices.Messaging.Tests/
  Microservices.Security.Tests/
```

Shared libraries are technical capabilities, not a common domain model. A service owns its
entities, migrations, consumers, saga state, and business integration contracts. When a real
service is created, rename `ServiceTemplate` and add slices such as
`Features/<Feature>/<Command|Query>` inside its API project.

## Identity and authorization

`Identity.Api` is the only service that owns users, credentials, Identity cookies, OpenIddict
applications/scopes/tokens, signing credentials, and the identity database. Account management is
implemented as vertical slices. `Microservices.Security` is deliberately a resource-server-only
library: it validates signed JWT access tokens through the issuer's discovery document and JWKS and
does not reference OpenIddict, Entity Framework Core, Npgsql, or `Identity.Api`.

Resource APIs call `AddApiSecurity`, configure `Security:Authority` and `Security:Audience`, and use
`ScopePolicy.For("<scope>")` on endpoints that need a capability. Authentication is the fallback
policy, so only intentionally public endpoints should call `AllowAnonymous`. Audience validation,
the `at+jwt` token type, signature, issuer, and lifetime are enforced. A scope grants an operation;
it never replaces resource ownership or tenant checks inside the owning service.

### Supported flows

- Customer browser clients use OpenID Connect authorization code with PKCE (`S256` only). Public
  clients have no secret. Prefer a backend-for-frontend for browser sessions; request
  `offline_access` only when a refresh token is actually needed.
- Server-side web clients use authorization code with PKCE and exactly one confidential-client
  credential: a secret from the secret store (`client_secret_basic`) or a public JSON Web Key Set
  for `private_key_jwt`. Secret-in-request-body authentication is disabled.
- Machine clients use OAuth 2.0 client credentials and receive no user identity.
- Password and implicit grants are intentionally unsupported. `Identity.Api` owns the secure
  application cookie, while a separately deployed interaction frontend owns the login, access-denied,
  logout-confirmation, confirmation, recovery, and MFA presentation. APIs accept bearer access
  tokens for protected resources; the Identity cookie is used only for authorization-server browser
  interactions.
- ASP.NET Identity authenticator and recovery-code completion are supported for accounts that have
  two-factor authentication enabled. Enrollment belongs in a separately authorized account slice.

Provisioned first-party clients use OpenIddict's pre-established (internally named `implicit`)
consent type. That is consent bookkeeping and does not enable the OAuth implicit grant.

OpenIddict publishes discovery and signing keys from `/.well-known/openid-configuration`. Protocol
endpoints are `/connect/authorize`, `/connect/par`, `/connect/token`, `/connect/revocation`,
`/connect/logout`, and `/connect/userinfo`. HTTPS is mandatory outside Development.
Pushed authorization is granted only to clients configured to require it.

Account operations are Web API endpoints. The frontend owns all application HTML. Email
confirmation is submitted as JSON to `POST /api/v1/accounts/email-confirmation`; registration and
password recovery use the corresponding versioned JSON endpoints. The Identity API never constructs
or returns application pages.

Interactive protocol handoffs use this contract:

1. An unauthenticated `/connect/authorize` request challenges the Identity application cookie.
2. The cookie challenge redirects to `IdentityInteraction:PublicOrigin` plus the configured login
   path and supplies only a validated local `returnUrl`.
3. The frontend submits credentials to the Identity API, receives the secure HTTP-only session
   cookie, then navigates to that local return URL so OpenIddict can finish authorization.
4. `GET /connect/logout` is validated and cached by OpenIddict, then redirects to the configured
   frontend logout page with a short-lived protected interaction token and a local completion URI.
5. After user confirmation, the frontend posts `application/x-www-form-urlencoded` with
   `interactionToken` to that completion URI. The API binds the token to the exact cached logout
   request, clears the Identity session, and lets OpenIddict perform the registered post-logout
   redirect.

The interaction frontend and API must be published behind the same HTTPS origin in production.
This keeps the cookie first-party and removes cross-site cookie assumptions while allowing the UI
and API to remain separate deployable components.

### Audiences and scopes

Scope configuration maps each capability to the API resource that will consume it; that resource is
emitted as the access token's `aud` claim.

| Audience | Capability scopes |
| --- | --- |
| `booking-public-api` | `flight.read`, `booking.read`, `booking.create`, `booking.cancel`, `passenger.self.read`, `passenger.self.update` |
| `identity-api` | `identity.profile.read` |

`openid` requests an OpenID Connect identity. `profile`, `email`, and `roles` release only their
corresponding claims when allowed; `offline_access` requests a refresh token. Clients must be
provisioned only for the smallest set they require, and an API must reject a token issued for a
different audience even if it contains a similarly named scope.

Access tokens are signed `at+jwt` values with a ten-minute default lifetime. They are intentionally
validated offline by APIs, so account deactivation or password reset stops new code/refresh-token
exchange immediately but an already issued access token remains valid until it expires. Use a
shorter lifetime or reference-token introspection for unusually high-risk operations.

## Run locally

Requirements: .NET 10 SDK and a container runtime supported by Aspire.

```bash
dotnet tool restore
dotnet restore
dotnet run --project src/AppHost/Microservices.AppHost
```

Aspire supplies generated local credentials and connection strings. The values in
`appsettings.Development.json` are development-only fallbacks. Production credentials and
license keys must come from the deployment secret store or environment variables.
`Microservices.AppHost` is a local-development orchestrator, not a production deployment model;
its localhost issuer is intentionally fixed to the local HTTPS endpoint.

Aspire first runs `identity-migrator`, which applies the identity schema and reconciles the configured
OpenIddict scopes and clients, then starts `Identity.Api` at the Development issuer
`https://localhost:7100/`. The development `booking-web` registration permits only the redirect and
post-logout URIs in `Identity.Api/appsettings.Development.json`; change those values to the exact
local frontend URLs when necessary. `IdentityInteraction:PublicOrigin` identifies the frontend that
renders browser interactions, and `AuthorizationServer:CorsOrigins` authorizes that exact origin to
call the credentialed Identity account/session endpoints. Registration and recovery notifications
are written to the Identity API log only in Development. Notification action URLs use
`IdentityNotifications:PublicOrigin` and point to the frontend application, which submits the token
to the Identity Web API. The sample API validates tokens for `booking-public-api` against this
issuer. Local OpenIddict signing/encryption keys are ephemeral by default, so local tokens
intentionally stop working after an Identity API restart; production always requires persistent
certificates.

## Migrations

Create a migration after changing a service-owned model:

```bash
dotnet ef migrations add <Name> \
  --project src/Services/ServiceTemplate/ServiceTemplate.Api \
  --startup-project src/Services/ServiceTemplate/ServiceTemplate.Api \
  --context ServiceTemplateDbContext \
  --output-dir Persistence/Migrations
```

Deploy `ServiceTemplate.Migrator` as a run-once task before rolling out API instances. Do not
call `Database.Migrate()` from the API process.

For an Identity model change, create a service-owned migration with:

```bash
dotnet ef migrations add <Name> \
  --project src/Services/Identity/Identity.Api \
  --startup-project src/Services/Identity/Identity.Api \
  --context IdentityServiceDbContext \
  --output-dir Persistence/Migrations
```

Deploy `Identity.Migrator` as one run-once task before Identity API replicas. It applies migrations
and authoritatively reconciles the scopes and clients marked as owned by this migrator. Stale owned
entries are deleted (which also invalidates their authorizations/tokens); operator-managed entries
are never touched. Pruning is deliberately skipped when either manifest is empty so a missing
configuration source cannot wipe registrations. Retiring the final managed client/scope therefore
requires an explicit operational deprovisioning step.

Give only the migrator its schema-owner connection and `AuthorizationServer:Scopes` plus
`AuthorizationServer:Clients` manifest, including confidential-client credentials. Identity API
replicas need the safe runtime scope metadata but do not need client manifests or client secrets.
The migrator deliberately does not load runtime signing certificates, Data Protection, cookies, or
notification credentials. Do not migrate or provision from API startup, and do not allow concurrent
deployment jobs to own schema rollout.

## Identity production configuration

The Identity API fails startup outside Development when required security settings are absent or
unsafe. Supply all secrets through the deployment secret provider, never committed JSON:

- Set `AuthorizationServer:Issuer` to the stable externally visible HTTPS issuer. Every API's
  `Security:Authority` must match it exactly and `Security:RequireHttpsMetadata` must remain `true`.
- Mount separate, currently valid RSA PFX files with private keys for
  `AuthorizationServer:SigningCertificates` and `AuthorizationServer:EncryptionCertificates`.
  Each certificate must use an RSA key of at least 3072 bits. Supply PFX passwords as secrets.
  Configure old and new certificates together during rotation; do not remove an old decryption
  certificate while persisted tokens or Data Protection keys need it.
- Give the migrator each `AuthorizationServer:Clients` entry with exact HTTPS redirect/logout URIs
  and least-privilege scopes. Public clients must have no credential. `Web` and `Service` clients
  require exactly one secret-manager-generated Base64url `ClientSecret` or a public-only
  `JsonWebKeySetPath`; the client's private key never enters this service. Enable refresh tokens
  and pushed authorization requests only for clients designed to use them.
- Set `IdentityInteraction:PublicOrigin` to the issuer's HTTPS origin and route its configured
  `/account/*` paths to the interaction frontend while routing `/connect/*` and `/api/*` to
  `Identity.Api`. Production startup rejects a different interaction origin. Keep protected
  interaction tokens short-lived; never replace them with an unvalidated external return URL.
- Configure only exact first-party origins in `AuthorizationServer:CorsOrigins`. Wildcards are not
  accepted. Credentialed CORS is enabled because the frontend must establish the Identity session
  cookie; production should normally list only the same public identity origin. Do not inject the
  client manifest or confidential-client secrets into API replicas.
- Mount a curated common/compromised-password blocklist and set its absolute path in
  `IdentityPasswordPolicy:BlocklistPath`. Production startup requires at least
  `IdentityPasswordPolicy:MinimumBlocklistEntries` distinct entries. Update the mounted dataset as
  part of the security-maintenance process; submitted passwords are checked locally and are never
  logged or sent to the notification provider.
- Set `IdentityNotifications:Provider` to `Webhook`, `PublicOrigin` to the public HTTPS frontend
  origin that owns confirmation and recovery pages, and `WebhookEndpoint` to the production
  notification service. Store a secret-manager-generated Base64url `WebhookApiKey` as a secret.
  `DevelopmentLog` is forbidden in production because confirmation and reset links contain one-time
  credentials. The frontend extracts the one-time values from the action URL and submits them to the
  versioned Identity API endpoint. The API commits an encrypted notification outbox record in the same
  database transaction as account creation; the worker uses leases, bounded retries, deduplication,
  dead-lettering, and an idempotency key. Alert on dead-lettered rows and repeated webhook failures.
- Provide `ConnectionStrings:identity-db` from the secret store. Data Protection keys are persisted
  in that database and encrypted with the configured encryption certificate, so backup and
  certificate-rotation procedures must preserve both. Use separate database roles: schema-owner
  permissions for the migrator and only the runtime DML permissions required by Identity API.
- Override the base `AllowedHosts` value with the public identity host. At a TLS-terminating proxy,
  set `ReverseProxy:Enabled` and configure an explicit `KnownProxies` and/or `KnownNetworks` allow
  list, the expected `ReverseProxy:AllowedHosts`, and the actual hop count in `ForwardLimit`.
  Forwarded headers from every other source are ignored. Never disable OpenIddict's HTTPS transport
  protection or issuer/audience validation.
- The included protocol and account limiters protect each process. Enforce a distributed or edge
  limit before multi-replica deployments and add credential-stuffing/bot detection appropriate to
  the threat model. Keep OpenIddict pruning enabled; replicas coordinate it with a PostgreSQL
  advisory lock.

## Messaging rules

- Outside a consumer, change business state and send or publish through the scoped
  `ISendEndpointProvider` or `IPublishEndpoint` before the same `SaveChangesAsync` call. The bus
  outbox persists both atomically and dispatches after commit. Publishing through `IBus` bypasses
  the scoped bus outbox.
- `AddRabbitMqWithPostgresOutbox<TDbContext>` supports registration-driven endpoints created by
  MassTransit's `ConfigureEndpoints`. Raw `ReceiveEndpoint` declarations are intentionally outside
  this shared path because they bypass its automatic Entity Framework Core inbox/outbox middleware.
- `TDbContext` is the service's single owned transactional context. Its model must call
  `AddMassTransitOutboxEntities`. Every automatically configured endpoint uses that context, and
  consumers must use the same scoped instance for business changes that must commit atomically with
  message consumption.
- Consumers use the EF inbox/outbox middleware and must still be idempotent at business level.
- Treat `endpointNamePrefix` as durable service topology. Choose a lowercase kebab-case value that
  is unique within the RabbitMQ virtual host, and use the same value for every replica. Never add a
  machine, pod, process, deployment-slot, or random suffix. Changing the prefix creates different
  queue names and requires an explicit topology migration. Isolate environments with separate
  brokers or virtual hosts.
- The shared library intentionally adds no blanket retry policy. Configure short retries only for
  explicitly identified transient exceptions in a consumer definition or endpoint callback. Use
  redelivery for longer delays. Poison messages go to MassTransit's error queue.
- TLS is required by default. Local development explicitly disables it; production should use an
  `amqps://` connection string or enable TLS in the `Messaging` section. Protocol negotiation follows
  operating-system security policy (`SslProtocols.None` does not disable TLS), while certificate
  presence, chain trust, and server-name matching remain enforced.
- Put a saga state machine in the service that owns the long-running workflow. Persist saga
  state in that service's PostgreSQL database. Do not place saga business logic in a shared lib.
- Add a saga only when the first multi-service workflow requires compensation; do not create an
  empty generic saga now.

## Licensing

This template uses current MediatR and MassTransit major versions. Configure
`Licensing__MediatR` and the MassTransit license according to the vendors' current deployment
instructions. Never commit license keys. Confirm commercial or community eligibility before
shipping.

## Recommended next steps

1. Rename the service template for the first bounded context.
2. Implement one end-to-end vertical slice with its validator and tests.
3. Add its service-owned integration contracts and consumer.
4. Add container images, deployment manifests, an external secret provider, edge rate limits,
   image/dependency scanning, and deployment promotion for the chosen hosting platform; provision
   production Identity clients, certificates, frontend routes, and password blocklist.
5. Add the product-specific, step-up-protected slices for authenticator/passkey enrollment,
   recovery-code regeneration, administrative user management, and any additional breached-password
   intelligence selected by the security team. The current service deliberately does not expose
   unsafe generic admin endpoints or bootstrap an administrator.
6. Add a persisted saga only when a concrete cross-service workflow is known.
