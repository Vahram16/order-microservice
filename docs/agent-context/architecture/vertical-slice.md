# Vertical Slice Architecture Contract

This repository uses feature-oriented vertical slices for business APIs. The concrete reference is `src/Services/Customer/Customer.Api/Features/Customers`.

## Canonical shape

A versioned use case lives under:

```text
Features/<Area>/<UseCase>/V1/
```

A slice may contain only the responsibilities that use case needs, typically:

```text
<Operation>Endpoint.cs
<Operation>Request.cs       # when transport input differs from command/query
<Operation>Command.cs       # mutation
<Operation>Query.cs         # read
<Operation>Validator.cs
<Operation>DataValidator.cs # only when a reusable nested request object needs its own validation
<Operation>Handler.cs
<Operation>Result.cs        # when application result differs from HTTP representation
<Operation>Response.cs      # when operation-specific response is needed
```

Do not force every slice to have every file. Mirror the nearest analogous slice.

## Enforced rules

`tests/Customer.Api.Tests/CustomerVerticalSliceArchitectureTests.cs` is executable architecture. For Customer slices it enforces:

- versioned `V1` directories;
- one top-level responsibility/type per source file;
- a static endpoint `Map(...)` method;
- commands using `ICommand<TResponse>` and queries using `IQuery<TResponse>`;
- handlers using `ICommandHandler<...>` or `IQueryHandler<...>`;
- no raw `IRequest` / `IRequestHandler` in business slices;
- no sibling-slice namespace dependencies;
- no monolithic `*Slice.cs` files.

Do not weaken those tests to accommodate an implementation unless the architecture itself is being deliberately changed and explicitly approved.

## Responsibility placement

### Endpoint

The endpoint owns transport concerns:

- route/method;
- request binding;
- authenticated principal extraction through approved infrastructure helpers;
- HTTP preconditions such as `If-Match` or idempotency headers when applicable;
- MediatR dispatch through `ISender`;
- authorization policy composition;
- HTTP response and Problem Details mapping;
- response headers such as ETag;
- OpenAPI metadata.

Canonical mutation: `UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs`.

### Command/query

A command/query is the application message for exactly one use case. It carries validated application inputs needed by the handler. It must not contain `HttpContext`, `ClaimsPrincipal`, `IResult`, EF entities, or transport infrastructure.

### Validator

FluentValidation is input defense. Validation does not replace domain invariants. The validator should reject malformed request/application values that are knowable without loading mutable business state.

### Handler

The handler orchestrates the use case:

- load required state through the owning service `DbContext`/approved infrastructure;
- translate aggregate absence and application preconditions;
- invoke domain behavior;
- coordinate transactions and persistence;
- translate known persistence conflicts;
- return semantic `Result`/`Result<T>` values.

The handler must not become a general application service reused by unrelated slices.

### Domain

Business invariants/state transitions belong in `Domain`, not in the endpoint or handler. See `domain-boundary.md`.

### Common

`Features/<Area>/Common` is for stable cross-slice primitives only. In Customer it contains HTTP/result mapping, response mapping, authorization constants, composable queries, and other intentionally shared behavior.

Promote code to `Common` only when at least two slices genuinely share the same stable concept. Do not create speculative abstractions for anticipated reuse.

## Canonical examples

Use these before searching widely:

- simple authenticated read: `GettingCurrent/V1/`;
- standard mutation with ETag: `UpdatingDetails/V1/`;
- idempotent/concurrent transactional mutation: `AddingAddress/V1/`;
- owned-child replacement: `UpdatingAddress/V1/`;
- deletion of owned child: `RemovingAddress/V1/`;
- export/read projection: `Exporting/V1/`;
- destructive business lifecycle operation: `ClosingAccount/V1/`;
- idempotent provisioning: `Provisioning/V1/`.

## New bounded contexts

For a new service/domain, copy only the structural discipline:

- service ownership;
- versioned slices;
- CQRS contracts;
- framework-free domain;
- explicit persistence/security/error boundaries;
- deterministic architecture tests.

Do not copy Customer entities, routes, ETags, scopes, idempotency semantics, lifecycle rules, or persistence constraints unless the new bounded context's requirements independently demand them.