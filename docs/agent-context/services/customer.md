# Customer Bounded Context

Use this document only for work owned by `src/Services/Customer`. It records Customer-specific placement, canonical examples, and service boundaries. Common architecture behavior remains in `../architecture/*.md` and should be loaded only when relevant.

## Ownership

```text
src/Services/Customer/
├── Customer.Api/
│   ├── Domain/
│   ├── Features/
│   ├── Infrastructure/
│   ├── Persistence/
│   ├── Program.cs
│   └── Customer.Api.csproj
└── Customer.Migrator/
    └── Customer.Migrator.csproj

tests/Customer.Api.Tests/
```

`Customer.Api` is the repository's concrete business-service reference implementation. Do not copy Customer business semantics into another bounded context; reuse only proven structural conventions.

## `Customer.Api` responsibilities

The API project owns the Customer bounded context's HTTP surface, application orchestration, domain model, persistence model/schema, service-local infrastructure, and composition root.

Current direct shared-project dependencies:

```text
Customer.Api
├── Microservices.Application
├── Microservices.Persistence.Postgres
├── Microservices.Primitives
├── Microservices.Security
└── Microservices.ServiceDefaults
```

A convenient shared library is not automatically an allowed dependency. Inspect `Customer.Api.csproj` and prove the boundary before adding a project reference.

## Domain

Path: `src/Services/Customer/Customer.Api/Domain`

Put here:

- Customer-owned aggregates/entities/value objects;
- business invariants and state transitions;
- semantic domain errors/outcomes;
- failure-atomic business behavior.

Do not put here ASP.NET Core, EF Core/Npgsql, MediatR/CQRS dispatching, FluentValidation, claims/header parsing, API idempotency vocabulary, MassTransit/RabbitMQ, persistence, or infrastructure plumbing.

Executable boundary: `tests/Customer.Api.Tests/CustomerDomainBoundaryTests.cs`.

Load `../architecture/domain-boundary.md` when changing these responsibilities.

## Features: pure versioned vertical slices

Path: `src/Services/Customer/Customer.Api/Features/Customers`

Canonical shape:

```text
Features/Customers/
├── Common/
└── <UseCase>/
    └── V1/
        ├── <UseCase>Endpoint.cs
        ├── <UseCase>Request.cs       when needed
        ├── <UseCase>Command.cs       for mutations
        ├── <UseCase>Query.cs         for reads
        ├── <UseCase>Validator.cs
        ├── <UseCase>Handler.cs
        ├── <UseCase>Result.cs        when needed
        └── <UseCase>Response.cs      when operation-specific
```

A use case owns its endpoint mapping, request contract, command/query, validation, handler, authorization behavior, and operation-specific response behavior.

Do not create horizontal `Services`, `Managers`, or generic repository/application-service layers merely to move logic out of a slice. Do not reference sibling use-case namespaces. Promote only stable cross-slice Customer helpers to `Features/Customers/Common`.

Canonical starting points:

- read/query: `Features/Customers/GettingCurrent/V1/`;
- ordinary mutation: `Features/Customers/UpdatingDetails/V1/`;
- idempotent/transactional mutation: `Features/Customers/AddingAddress/V1/`;
- owned-child update/delete: `Features/Customers/UpdatingAddress/V1/`, `RemovingAddress/V1/`;
- lifecycle/destructive business operation: `Features/Customers/ClosingAccount/V1/`.

Architecture enforcement: `tests/Customer.Api.Tests/CustomerVerticalSliceArchitectureTests.cs`.

Load `../architecture/vertical-slice.md` for slice rules and then only the additional architecture references actually affected.

## Persistence

Path: `src/Services/Customer/Customer.Api/Persistence`

Owns:

- `CustomerDbContext`;
- Customer EF mappings;
- Customer-owned PostgreSQL constraints/indexes;
- Customer migrations;
- persistence-only helpers and known provider-conflict translation where Customer-owned.

Canonical entry point: `Persistence/CustomerDbContext.cs`.

Business invariants remain in Domain even when a database constraint also defends them. Preserve defense in depth where the current consistency model requires it.

Load `../architecture/persistence.md` for EF/schema work and `../architecture/concurrency-idempotency.md` when races, ETags, idempotency, explicit transactions, or retry/reload semantics are involved.

## Infrastructure

Path: `src/Services/Customer/Customer.Api/Infrastructure`

Use for Customer-specific infrastructure adapters/helpers that are stable across multiple Customer slices but are not general enough for `src/Shared`.

Do not use `Infrastructure` as a miscellaneous folder. If only one slice owns the helper, keep it in that slice unless there is a demonstrated service-wide responsibility.

## Composition root

`Customer.Api/Program.cs` owns service composition and endpoint-group registration. Do not put business behavior in `Program.cs`.

## Customer Migrator

`src/Services/Customer/Customer.Migrator` is the run-once deployment migration process. It references `Customer.Api` so it can execute the service-owned EF model/migrations plus PostgreSQL/ServiceDefaults support.

Deployment order:

```text
Customer.Migrator succeeds
        ↓
Customer.Api replicas start/roll
```

The API process must not apply schema migrations during startup.

## Error / concurrency / idempotency conventions

Do not infer these from generic .NET conventions. Load the corresponding architecture reference and nearest Customer implementation:

- HTTP/error mapping -> `../architecture/api-and-errors.md`;
- ETag/concurrency/idempotency/transaction behavior -> `../architecture/concurrency-idempotency.md`;
- persistence -> `../architecture/persistence.md`;
- security/authorization -> `../architecture/security.md` when affected;
- messaging -> `../architecture/messaging.md` only when Customer begins/changes integration messaging behavior.

`AddingAddress/V1/AddCustomerAddressHandler.cs` is the canonical complex mutation for idempotency, transaction, race, and reload behavior.

## Tests

Primary project: `tests/Customer.Api.Tests`.

It owns Customer domain, vertical-slice architecture, API integration, persistence, concurrency/idempotency, error behavior, and service-specific verification.

For a Customer change, inspect the nearest production slice and corresponding tests before introducing a new pattern. Repository-wide dependency constraints may also require `tests/Microservices.ArchitectureTests`.

## What Customer does not own automatically

Do not place a concern in Customer merely because it is used by Customer. In particular, do not silently move these boundaries:

- identity-provider responsibilities -> Keycloak/platform security;
- stable cross-service contracts -> `Microservices.Contracts` only when a real cross-service contract exists;
- MassTransit/RabbitMQ transport implementation -> `Microservices.Messaging`;
- reusable platform defaults -> `Microservices.ServiceDefaults`;
- speculative cross-service helpers -> nowhere until reuse is demonstrated.

If a Customer task unexpectedly requires another service/shared owner, a new project reference, a durable integration contract, security configuration, or a new shared abstraction, treat that as plan/context drift and re-evaluate before implementation.