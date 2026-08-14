# Product Bounded Context

Use this document only for work owned by `src/Services/Product`. Common architecture behavior remains in `../architecture/*.md` and should be loaded only when the affected boundary requires it.

## Ownership

```text
src/Services/Product/
├── Product.Api/
│   ├── Domain/
│   ├── Features/
│   ├── Persistence/
│   ├── Program.cs
│   └── Product.Api.csproj
└── Product.Migrator/
    └── Product.Migrator.csproj

tests/Product.Api.Tests/
```

Product owns its catalog behavior, Product domain model, HTTP contracts, persistence model and PostgreSQL database, migrations, and service-specific tests. It must not depend on Customer data or persistence.

## Business-service structure

`Product.Api` is a real bounded-context service. Derive its pure versioned vertical-slice and domain-boundary conventions from the concrete Customer service, while keeping Product semantics local.

Place Product aggregate behavior and invariants under `Domain`. Keep those types free of ASP.NET Core, EF Core/Npgsql, MediatR, FluentValidation, security plumbing, and transport dependencies.

Place each HTTP use case under:

```text
Features/Products/<UseCase>/V1/
```

Each slice owns its endpoint, request when needed, command or query, validator, handler, and operation-specific result/response. Do not introduce horizontal repository, manager, or application-service layers merely to hide EF Core or reshape a slice.

## Persistence and migration lifecycle

`Product.Api/Persistence` owns `ProductDbContext`, Product mappings and constraints, and Product migrations. The connection-string resource name is `product-db`.

`Product.Migrator` is the run-once deployment migration process:

```text
Product.Migrator succeeds
        ↓
Product.Api replicas start/roll
```

The API process must not apply migrations during startup.

## Runtime boundaries

Product currently has no approved integration messaging behavior. Do not add RabbitMQ, MassTransit, outbox/inbox entities, shared integration contracts, or Order dependencies until a later approved task defines them.

The API uses the shared authentication fallback policy. Local development configures an isolated `product-api` audience/resource client and a `product-scalar-dev` public PKCE client. Product capability scopes and application roles are intentionally not invented in this change. Any capability or privilege contract is separate security work requiring explicit review.

## Tests

Primary project: `tests/Product.Api.Tests`.

It owns Product domain, pure vertical-slice architecture, request validation, HTTP behavior, persistence-model, migration, and PostgreSQL integration coverage. Repository-wide dependency and messaging-leakage rules remain in `tests/Microservices.ArchitectureTests`.

## Context routing

Load only the affected references:

- business endpoint/use case -> `../architecture/vertical-slice.md`;
- domain invariant/value object -> `../architecture/domain-boundary.md`;
- HTTP/error behavior -> `../architecture/api-and-errors.md`;
- EF model/schema/migration -> `../architecture/persistence.md`;
- authentication/authorization -> `../architecture/security.md`;
- verification -> `../testing-map.md` and `../architecture/testing.md` when detailed semantics are needed.

Do not load messaging architecture for ordinary Product CRUD work while messaging remains out of scope.
