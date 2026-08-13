# ServiceTemplate Context

Use this document only for changes owned by `src/Services/ServiceTemplate` or when an approved task creates a new real service from the repository's platform scaffolding.

`ServiceTemplate` is **infrastructure/service scaffolding**. It is not the canonical source of Customer business semantics and it does not prove that an Order bounded context exists.

## Projects

```text
src/Services/ServiceTemplate/
├── ServiceTemplate.Api/
│   └── ServiceTemplate.Api.csproj
└── ServiceTemplate.Migrator/
    └── ServiceTemplate.Migrator.csproj
```

Current direct shared-project dependencies of `ServiceTemplate.Api`:

```text
ServiceTemplate.Api
├── Microservices.Application
├── Microservices.Contracts
├── Microservices.Messaging
├── Microservices.Persistence.Postgres
├── Microservices.Security
└── Microservices.ServiceDefaults
```

The template demonstrates how a service composes generic platform capabilities. It does **not** authorize every future service to depend on every shared project. New services should reference only capabilities required by approved requirements.

## API project

Use the API project as a platform composition example for:

- shared application/CQRS registration;
- PostgreSQL service wiring;
- shared security/token-validation registration;
- ServiceDefaults/observability/health/OpenAPI wiring;
- messaging/outbox infrastructure when the real service actually requires messaging.

For real business-service structure, domain invariants, and pure vertical slices, use the concrete Customer service plus shared architecture references instead of inventing placeholder domain behavior from the template.

## Migrator

`ServiceTemplate.Migrator` is a run-once migration process paired with `ServiceTemplate.Api`.

The deployment invariant is the same as other services:

```text
<Service>.Migrator succeeds
        ↓
<Service>.Api replicas start/roll
```

API startup must not apply database migrations.

## Creating a new real bounded context

A new service is justified only by explicit approved business ownership. Do not create `Order`, `Inventory`, `Payment`, or another service because the repository name/template makes it plausible.

When a new bounded context is genuinely approved:

1. define the bounded-context responsibility and data ownership first;
2. create a service-specific context document under `docs/agent-context/services/` as part of the architecture work;
3. derive platform wiring from ServiceTemplate;
4. derive business slice/domain conventions from Customer and shared architecture references;
5. create the paired API + Migrator only when persistence is required;
6. add only the shared-project dependencies the service actually needs;
7. add service-specific tests and repository architecture checks;
8. update `Microservices.Boilerplate.slnx`, AppHost only for local orchestration, and CI as required;
9. treat new cross-service contracts, security scopes/roles, messaging topology, and production infrastructure as explicit architecture/security decisions rather than template defaults.

Do not copy placeholder/example business names, contracts, queues, scopes, or data semantics from scaffolding.

## Context routing

Load only the relevant common references:

- business endpoint/use-case structure -> `../architecture/vertical-slice.md`;
- domain rules -> `../architecture/domain-boundary.md`;
- EF/schema/migrations -> `../architecture/persistence.md`;
- messaging -> `../architecture/messaging.md`;
- security -> `../architecture/security.md`;
- verification -> `../testing-map.md` and `../architecture/testing.md` when needed.

For shared library ownership, load `../platform/shared-projects.md`. For local Aspire changes, load `../platform/apphost.md`.