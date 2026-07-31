# API documentation and background-service lifetimes

## Process-specific service defaults

Composition roots use an explicit defaults profile:

- `AddWebApiDefaults()` adds the common host baseline plus reverse-proxy trust,
  ASP.NET Core telemetry, and platform health checks.
- `AddJobDefaults()` adds only the common host baseline for run-once processes such as
  database migrators.
- `AddHostDefaults()` is the idempotent common building block for future process profiles.

`AddServiceDefaults()` remains as a compatibility entry point, but new composition roots should
state their process profile explicitly. A job host must be started and stopped around its work so
options validation, signal handling, and telemetry provider startup and flush actually run.

The shared outbound HTTP policy retries only safe methods. `POST`, `PUT`, `PATCH`, `DELETE`, and
`CONNECT` are not retried by default because doing so can duplicate side effects. A named client may
opt into write retries only when the downstream operation has an enforced idempotency contract.

The web health contract keeps the established URLs while separating their meaning:

- `/alive` evaluates only checks tagged `ServiceHealthCheckTags.Liveness`;
- `/health` evaluates only checks tagged `ServiceHealthCheckTags.Readiness`.

Required dependencies must be tagged for readiness deliberately. The lifecycle readiness check is
unhealthy before host startup completes and flips to unhealthy when shutdown begins. Both probe
routes are anonymous by design and must be exposed only through the deployment's management or
trusted ingress boundary.

Host and telemetry policy can be configured under `ServiceDefaults`:

```json
{
  "ServiceDefaults": {
    "ShutdownTimeout": "00:00:30",
    "Telemetry": {
      "IncludeFormattedLogMessage": false,
      "IncludeLogScopes": false,
      "TraceSamplingRatio": 0.1
    }
  }
}
```

Production trace sampling defaults to 10 percent; development defaults to full sampling unless the
ratio is explicitly configured. Standard `OTEL_TRACES_SAMPLER` configuration takes precedence.
Formatted log messages and scopes are opt-in because they can duplicate structured data or export
sensitive context. OTLP export recognizes both `OTEL_EXPORTER_OTLP_ENDPOINT` and the signal-specific
logs, metrics, and traces endpoint variables. `OTEL_SERVICE_NAME` overrides the application-name
resource identity.

The checked-in API `AllowedHosts` values are loopback-only development defaults. Every non-local
deployment must override them with its service DNS and public ingress hosts. When forwarded headers
are enabled, those values must remain consistent with explicit `ReverseProxy:AllowedHosts`,
`KnownProxies`, or `KnownNetworks`; catch-all trusted proxy addresses and networks are rejected.

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

Scalar's development OAuth flow uses the `scalar-dev` public Keycloak client, Authorization Code
with PKCE `S256`, and the realm configured by `Security:Authority`. The local realm permits only
the exact HTTPS callback `https://localhost:7040/scalar/v1`; use the API's HTTPS launch profile for
interactive sign-in. Scalar requests only interactive identity and Order API scopes; it does not
request `offline_access` or an offline refresh token.

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
