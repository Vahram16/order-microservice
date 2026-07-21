# API documentation and background-service lifetimes

## Scalar and OpenAPI

`Microservices.ServiceDefaults` exposes two explicit web-host extensions:

- `AddApiDocumentation(title)` registers the ASP.NET Core OpenAPI document and shared transformers.
- `MapApiDocumentation()` maps the OpenAPI JSON document and Scalar API Reference only in `Development`.

The shared operation transformer applies the bearer security requirement to every endpoint that is not explicitly marked `AllowAnonymous`. This matches the repository rule that authentication is the fallback policy and public endpoints must be deliberately annotated.

Development routes use the framework defaults:

- `/openapi/v1.json`
- `/scalar/v1`

The documentation endpoints are never mapped outside `Development`. They are excluded from their own OpenAPI document, send `no-store` headers, and use a documentation-specific Content Security Policy. Scalar is developer tooling; it does not change the application-HTML boundary of `Identity.Api`.

## Hosted-service dependency injection

A hosted service is a singleton. It must not capture an EF Core `DbContext`, an HTTP transport registered as scoped, or any other scoped dependency.

`IdentityNotificationOutboxWorker` therefore owns only:

- dispatch cadence and startup jitter,
- hourly retention-cleanup scheduling,
- creation and disposal of a DI scope,
- cycle-level exception logging.

Within each cycle it resolves one scoped `IdentityNotificationOutboxDispatcher`. The dispatcher receives these dependencies through normal constructor injection:

- `IdentityServiceDbContext`,
- `IIdentityNotificationTransport`,
- `IDataProtectionProvider`,
- notification options,
- `TimeProvider`,
- its typed logger.

The dispatcher owns leasing, decryption, delivery, retry classification, dead-lettering, retention cleanup, and persistence. A persistence failure after an external delivery is allowed to escape to the worker so the database lease expires naturally; downstream delivery remains safe through the notification idempotency key.
