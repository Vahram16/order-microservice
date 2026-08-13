# API and Error Architecture

Business APIs use Minimal APIs with explicit versioned vertical-slice endpoints. HTTP is a presentation boundary over semantic application/domain results.

## Endpoint responsibilities

A Customer endpoint typically:

1. extracts validated identity through `CurrentIdentity.From(principal)`;
2. parses required HTTP preconditions/headers through shared Customer HTTP helpers;
3. constructs one command/query;
4. dispatches through `ISender`;
5. maps `Result` success/failure to HTTP;
6. writes ETags or other response headers when required;
7. declares authorization and OpenAPI response metadata.

Canonical mutation: `UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs`.

Do not put persistence queries, aggregate mutation logic, or database conflict handling in endpoints.

## Authorization composition

Customer endpoints compose required role and capability scope explicitly, for example with `RolePolicy.For(...)` and `ScopePolicy.For(...)`. Authentication is a fallback policy at the platform level, so public endpoints must be intentionally anonymous.

Never treat a route/body identity value as authoritative when the validated token identity owns the resource.

## Error pipeline

The intended flow is:

```text
Domain semantic failure
        ↓
Application contextual translation (when needed)
        ↓
OperationError
        ↓
CustomerErrorCatalog descriptor
        ↓
CustomerHttpResults.Problem(...)
        ↓
RFC-style Problem Details + stable code + retryability + trace identifier
```

`CustomerHttpResults` is the canonical Customer presentation mapper. It rejects error metadata that collides with reserved Problem Details properties.

The shared platform pipeline in `Microservices.ServiceDefaults/ProblemDetails` handles validation, unsupported behavior, status-code fallbacks, and safe unknown exceptions.

## Separation rules

- Domain errors do not contain HTTP status codes.
- Application handlers do not return `IResult` or `ProblemDetails`.
- Endpoint/presentation code does not infer business invariants from exception messages.
- Exception messages/stack traces are never copied to client-facing error detail.
- Known persistence conflicts are translated intentionally; unknown exceptions follow the safe 500 path.
- Error codes/types are durable API behavior. Do not casually rename them.

## HTTP concurrency

Customer state-changing operations except initial provisioning require the latest strong ETag using `If-Match`. Missing, malformed, and stale preconditions have distinct semantics. See `concurrency-idempotency.md` before modifying those flows.

## Response privacy

Customer responses use no-store/no-cache behavior documented in `docs/customer-service.md`. Do not weaken privacy headers for PII-bearing endpoints without explicit requirements and security review.

## Canonical evidence

- `src/Services/Customer/Customer.Api/Features/Customers/UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs`;
- `src/Services/Customer/Customer.Api/Features/Customers/Common/CustomerHttp.cs`;
- `CustomerHttpResults.cs`;
- `CustomerErrorCatalog.cs`;
- `CustomerApplicationErrors.cs`;
- `docs/error-handling.md`;
- `tests/Customer.Api.Tests/CustomerHttpResultsTests.cs`;
- `CustomerRequestValidationTests.cs`;
- `CustomerApiIntegrationTests.cs`;
- `tests/Microservices.ServiceDefaults.Tests/ProblemDetailsPipelineTests.cs`.

## Review questions

1. Is HTTP logic limited to presentation concerns?
2. Are error codes/status mappings compatible with existing clients?
3. Could any exception or diagnostic information leak to the client?
4. Are required role/scope policies still present?
5. Are identity and ownership derived from trusted claims rather than caller-supplied IDs?
6. Are documented response statuses/OpenAPI metadata aligned with real behavior?