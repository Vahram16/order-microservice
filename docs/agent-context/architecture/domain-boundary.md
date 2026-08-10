# Domain Boundary

The Customer domain is the concrete example of the repository's business-domain boundary. Domain source lives inside the owning service project under `Domain/`, but architecture tests keep it framework-independent.

## Domain owns

Domain code owns business meaning that remains true regardless of HTTP, persistence, or messaging technology:

- aggregate and value-object invariants;
- valid state transitions;
- aggregate-owned child identity and lifecycle;
- failure atomicity of business mutations;
- domain-specific semantic errors;
- business version evolution when the aggregate model requires it.

Expected domain failures return `Result` / `Result<T>` semantics rather than using exceptions as control flow.

## Domain does not own

`CustomerDomainBoundaryTests.cs` prohibits the Customer domain from referencing:

- ASP.NET Core;
- EF Core;
- MediatR;
- FluentValidation;
- Npgsql;
- `Microservices.Application`;
- `Microservices.Security`;
- `Microservices.ServiceDefaults`;
- Customer feature, persistence, or infrastructure namespaces.

Therefore domain code must not know about:

- HTTP methods/status codes/headers/Problem Details;
- authenticated claims or `ClaimsPrincipal`;
- `If-Match` syntax or `Idempotency-Key` headers;
- database constraint names or `DbUpdateException`;
- MassTransit/RabbitMQ;
- endpoint DTOs;
- Jira/task metadata.

## Placement examples

Use this boundary when deciding where a rule belongs:

| Concern | Owner |
| --- | --- |
| Customer is inactive and cannot mutate | Domain |
| Country code is not a valid domain value | Domain |
| Customer aggregate does not exist in storage | Application handler |
| Access token lacks required identity claim | Presentation/application infrastructure |
| `If-Match` is missing/malformed | HTTP/application boundary |
| Aggregate expected version is stale | Domain/application concurrency semantics |
| PostgreSQL uniqueness violation occurred | Persistence/application boundary |
| Error becomes HTTP 409/412/etc. | Presentation |
| API idempotency key was reused with different request semantics | Application interpretation over domain identity/state |

## Failure atomicity

A failed domain operation must not partially mutate aggregate state. When adding or changing an invariant, test both the failure result and the unchanged state after failure.

Do not move complex invariants into FluentValidation because it is easier to test. Input validation is defense in depth; the domain remains authoritative for business validity.

## Domain errors

Domain errors describe business semantics and use public-safe descriptions. They should not embed:

- HTTP status terminology;
- exception messages;
- database/provider terminology;
- authentication/header terminology;
- operational diagnostics.

Application code may translate a domain error into a more contextual application error when it has information the domain intentionally does not know.

## Canonical evidence

Inspect before changing domain behavior:

- `src/Services/Customer/Customer.Api/Domain/Customer.cs`;
- `CustomerAddress.cs` and `CountryCode.cs` for value/child behavior;
- `CustomerErrors.cs` for domain semantic errors;
- `tests/Customer.Api.Tests/CustomerDomainBoundaryTests.cs`;
- `tests/Customer.Api.Tests/CustomerDomainTests.cs`;
- `tests/Customer.Api.Tests/CustomerFlowReviewTests.cs` when changing end-to-end business flow semantics.

## Review questions

Before completing a domain change, answer:

1. Is this truly a business invariant rather than transport/persistence policy?
2. Can the rule be tested without ASP.NET Core/EF Core?
3. Does failure leave the aggregate unchanged?
4. Is the returned error semantic and client-safe?
5. Did the change accidentally couple domain code to a sibling slice or shared infrastructure?
6. Does persistence still faithfully enforce/store the domain model without becoming the source of business truth?