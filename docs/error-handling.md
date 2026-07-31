# Error handling architecture

## Decision

The solution uses explicit results for expected business and application outcomes, and exceptions for defects or failures that callers cannot correct.

```text
Expected domain rejection       -> Result / OperationError
Expected application rejection  -> Result / OperationError
HTTP representation             -> presentation mapping only
Programming or invariant defect -> exception
Unexpected infrastructure fault -> exception
Cancellation                    -> exception
```

Customer domain code lives under `Customer.Api/Domain` inside the Customer API project. It is a source-level boundary within the service, not a reusable package or separate deployable component. Architecture tests prevent domain files from referencing ASP.NET Core, MediatR, FluentValidation, EF Core, application features, persistence, or infrastructure.

## Result contract

`Result<T>` requires explicit success creation through `Result.Success(value)`. Successful reference-type results cannot contain `null`; absence must be represented deliberately by an optional value, a missing-resource error, or another use-case-specific contract. `OperationError` contains only:

- a stable code;
- a semantic `ErrorCategory`;
- a public, client-safe description;
- explicitly approved public metadata.

Diagnostic exception text, database details, secrets, stack traces, and internal identifiers are not stored in `OperationError` and are never copied automatically into HTTP responses.

## Semantic categories

| Category | Meaning | Default HTTP status |
| --- | --- | ---: |
| `InvalidInput` | Caller supplied an invalid value | 400 |
| `MissingResource` | Requested application resource is absent | 404 |
| `StateConflict` | Request conflicts with current business state | 409 |
| `ConcurrencyConflict` | Supplied representation is stale | 412 |
| `AuthenticationRequired` | Valid authentication is absent | 401 |
| `AuthorizationDenied` | Authenticated caller lacks permission | 403 |
| `PreconditionRequired` | Required request precondition is absent | 428 |
| `Unexpected` | Safe representation of an internal failure | 500 |

The presentation layer owns HTTP mapping. Domain code does not use HTTP status names or Problem Details types.

## Ownership

### Domain

The domain owns invariant and aggregate-child outcomes such as:

- invalid email or country code;
- inactive-customer mutation;
- address limit reached;
- address not owned by the aggregate;
- address identity conflict;
- aggregate version mismatch.

The domain does not own aggregate lookup, HTTP headers, authentication, database constraints, or idempotency-key terminology.

### Application

The application owns:

- customer aggregate not found;
- missing or invalid request preconditions;
- authentication claim extraction;
- idempotency-key reuse;
- known persistence and concurrency race translation.

When an address identity conflict is caused by a reused API idempotency key, the application translates the domain error into the public idempotency error.

### Request validation boundary

FluentValidation is limited to the application request boundary. It validates whether a command or query is structurally safe to dispatch, including required request fields, lengths, formats, and request-only relationships. A request validator must not perform aggregate lookups or become the authoritative implementation of a domain invariant. Authentication claims are not request fields; missing or invalid claims follow the authentication outcome instead of producing request-validation errors.

Domain factories and aggregates remain authoritative for business invariants, even when a boundary validator repeats a cheap check to provide earlier, field-oriented feedback. Domain rejection is represented by `Result` and `OperationError`, so the same invariant is enforced for callers that do not pass through MediatR or FluentValidation.

`ValidationException` is one deliberate exception to the general rule that expected outcomes use results. `ValidationBehavior` aggregates boundary failures and throws it only to short-circuit the MediatR pipeline. The centralized exception handler recognizes that specific exception and converts it to the standard `400 request.validation_failed` response. It must not be used for domain rejection, authentication, infrastructure failure, or as a general application control-flow mechanism.

### Internal invariant path

Audit construction receives aggregate identifiers, validated identity claims, application-owned action constants, and aggregate versions. Invalid values on that path indicate a programming, configuration, or corrupt-state defect. Audit construction therefore throws instead of returning a client-correctable validation result.

## Problem Details

`Microservices.ServiceDefaults` provides the solution-wide operational pipeline:

```csharp
builder.Services.AddMicroserviceProblemDetails();
app.UseMicroserviceProblemDetails();
app.MapMicroserviceErrorCatalog();
```

It provides:

- FluentValidation `400` Problem Details;
- safe logged `500` Problem Details for unhandled exceptions;
- body-producing status-code pages for framework-generated failures;
- trace correlation;
- a JSON fallback when content negotiation cannot select a Problem Details writer;
- a versioned, resolvable platform catalog under `/errors/v1/platform/{code}`.

Customer-specific errors are registered in a required versioned catalog under `/errors/v1/customer/{code}`. Each entry documents its stable code, title, status, description, and retry guidance. Mapping an unregistered error or a code with the wrong semantic category is treated as a programming defect and follows the safe `500` path.

## Verification

CI verifies:

- the Customer domain source boundary and same-assembly placement;
- result success/failure and non-null invariants;
- public metadata defensive copying;
- domain failure atomicity;
- internal audit invariant exceptions;
- customer and platform error catalogs;
- validation Problem Details;
- framework-generated 401, 403, 404, and 405 bodies;
- unsupported `Accept` fallback;
- safe 500 responses without exception-message disclosure;
- PostgreSQL concurrency and idempotency behavior.
