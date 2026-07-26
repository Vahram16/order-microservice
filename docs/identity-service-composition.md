# Identity service composition

`Identity.Api` is one independently deployable microservice. The registration classes described here are internal composition modules; they are not separate services, processes, layers, or deployment units.

## Public composition surface

The host uses two public entry points:

- `AddIdentityService()` registers the runtime Identity API.
- `AddIdentityPersistence()` registers the reduced persistence/provisioning graph used by the migrator.

`IdentityServiceExtensions` is intentionally a facade. It validates configuration and delegates to capability-owned registration classes in a fixed order:

1. persistence and OpenIddict stores,
2. ASP.NET Core Identity, cookies, certificates, and Data Protection,
3. OpenIddict server/validation, authorization, CORS, and protocol rate limiting,
4. notification provider graph,
5. maintenance options and hosted services.

This order is part of the service composition contract. Capability registration types remain `internal` so feature code cannot treat the composition root as an application API.

## Capability ownership

- `Persistence/IdentityPersistenceRegistration.cs` owns the Identity database and OpenIddict EF Core store registration.
- `Security/IdentitySecurityRegistration.cs` owns ASP.NET Core Identity security settings, cookies, password hashing, certificates, and Data Protection.
- `Features/Authorization/IdentityAuthorizationRegistration.cs` owns the authorization server, local token validation, authorization policies, browser CORS, and protocol rate limiting.
- `Notifications/IdentityNotificationRegistration.cs` owns the complete provider-specific notification dependency graph.
- `Maintenance/IdentityMaintenanceRegistration.cs` owns pruning options, scoped operations, and the maintenance worker.

Registration modules may depend on service-owned implementation types. Vertical feature handlers must depend on intent-focused contracts and must not resolve services manually.

## Notification boundary

Identity owns notification intent and durable enqueueing. It does not own provider-specific email delivery.

For the webhook provider, one registration module atomically registers:

- `OutboxIdentityNotificationSender` as the scoped application-facing enqueue implementation,
- `WebhookIdentityNotificationTransport` and `IIdentityNotificationTransport`,
- `IdentityNotificationOutboxDispatcher` as scoped work,
- `IdentityNotificationOutboxWorker` as the singleton scheduler.

The hosted worker creates a scope for every dispatch cycle and never captures `IdentityServiceDbContext` or another scoped dependency. The dispatcher owns database leasing, payload decryption, delivery classification, retries, dead-lettering, cleanup, and persistence.

Development logging registers only `DevelopmentIdentityNotificationSender`; it must not start the outbox worker or register webhook dependencies.

Actual SMTP, SendGrid, SES, or other provider code belongs in the separately deployable notification system behind the webhook contract. It should not be added to a shared library or injected directly into account slices.

## Dependency injection invariants

- Provider-specific dependency graphs are registered in exactly one place.
- Hosted services are singleton schedulers and create scopes for scoped work.
- EF Core contexts are scoped and never captured by hosted services.
- External delivery is idempotent and retry-safe.
- Production secrets remain configuration/secret-store concerns and never enter feature contracts.
- Browser CORS uses an exact allow-list, credentials, explicit methods and headers, and a bounded preflight cache.
- Shared projects do not reference `Identity.Api`.

Tests should verify complete provider graphs and service lifetimes rather than checking only one registration in isolation.
