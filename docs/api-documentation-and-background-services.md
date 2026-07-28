# API documentation and background-service lifetimes

## Scalar and OpenAPI

`Microservices.ServiceDefaults` exposes two explicit web-host extensions:

- `AddApiDocumentation(title)` registers the ASP.NET Core OpenAPI document and shared transformers.
- `MapApiDocumentation()` maps OpenAPI JSON and Scalar API Reference only in `Development`.

The shared operation transformer applies the bearer security requirement to every endpoint that is
not explicitly marked `AllowAnonymous`. This matches the repository rule that authentication is the
fallback policy and public endpoints must be deliberately annotated.

Development routes use the framework defaults:

- `/openapi/v1.json`
- `/scalar/v1`

The documentation endpoints are never mapped outside `Development`. They are excluded from their
own OpenAPI document, send `no-store` headers, and use a documentation-specific Content Security
Policy.

## Hosted-service dependency injection

A hosted service is a singleton. It must not capture an EF Core `DbContext`, an HTTP transport
registered as scoped, or any other scoped dependency.

A production worker should own only scheduling, cancellation, scope creation, and cycle-level
logging. Each cycle creates an `IServiceScope`, resolves one scoped dispatcher, invokes it, and then
disposes the scope. The scoped dispatcher owns database leasing, transactions, external calls,
retry classification, dead-lettering, and persistence.

External delivery must be idempotent because a process can fail after a remote system accepts work
but before the local transaction records success.
