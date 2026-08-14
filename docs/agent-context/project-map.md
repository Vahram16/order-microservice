# Project Ownership Map

Use this file as the **small, first-hop router** for repository ownership. It answers only: _which component owns this change, and which scoped context should be loaded next?_

Do not load every service/platform document. After ownership is clear, load only the matching scoped context plus behavior-specific architecture references.

## Solution ownership

| Change area | Owner | Load next |
| --- | --- | --- |
| Customer business behavior, Customer domain, Customer persistence | `src/Services/Customer` | `services/customer.md` |
| Product business behavior, Product domain, Product persistence | `src/Services/Product` | `services/product.md` |
| Generic service scaffolding/template wiring | `src/Services/ServiceTemplate` | `services/service-template.md` |
| Shared CQRS/contracts/messaging/PostgreSQL/security/service defaults | `src/Shared` | `platform/shared-projects.md` |
| Local Aspire orchestration/development resource wiring | `src/AppHost/Microservices.AppHost` | `platform/apphost.md` |
| Keycloak/RabbitMQ/observability deployment assets | `infrastructure` | `platform/infrastructure.md` |
| Test ownership / deterministic verification routing | `tests` / CI | `testing-map.md` |
| Agent workflow/contracts | `.agents`, `.automation`, `.codex`, `docs/agent-context` | `context-selection-contract.md` |

## Current solution projects

The authoritative solution is `Microservices.Boilerplate.slnx`.

```text
src/AppHost/Microservices.AppHost

src/Services/Customer/
├── Customer.Api
└── Customer.Migrator

src/Services/Product/
├── Product.Api
└── Product.Migrator

src/Services/ServiceTemplate/
├── ServiceTemplate.Api
└── ServiceTemplate.Migrator

src/Shared/
├── Microservices.Application
├── Microservices.Contracts
├── Microservices.Messaging
├── Microservices.Persistence.Postgres
├── Microservices.Primitives
├── Microservices.Security
└── Microservices.ServiceDefaults

tests/
├── Customer.Api.Tests
├── Microservices.Application.Tests
├── Microservices.ArchitectureTests
├── Microservices.Messaging.Tests
├── Microservices.Primitives.Tests
├── Microservices.Security.Tests
├── Microservices.ServiceDefaults.Tests
└── Product.Api.Tests
```

## Routing rules

- If the task names Customer or modifies files under `src/Services/Customer`, load `services/customer.md`.
- If the task names Product or modifies files under `src/Services/Product`, load `services/product.md`.
- If the task is about creating/modifying generic service scaffolding, load `services/service-template.md`.
- If the task proposes `src/Shared` changes or a new cross-service abstraction, load `platform/shared-projects.md` and treat the change as architecture-sensitive.
- If the task changes local development orchestration/resources, load `platform/apphost.md`.
- If the task changes broker/identity/observability deployment assets, load `platform/infrastructure.md`.
- Load `testing-map.md` when choosing verification ownership; detailed testing semantics remain in `architecture/testing.md`.
- Inspect the owning `.csproj` before proposing any project/package reference change.

## Hard boundaries

- Repository naming does **not** imply an existing Order bounded context. Do not invent Order domain/service behavior without approved requirements.
- A business service owns its domain, API behavior, persistence model/schema, and migrations.
- `src/Shared` is for demonstrated stable cross-service concerns, not speculative reuse.
- AppHost is local development orchestration, not production business logic.
- API processes do not apply migrations at startup; service Migrators execute deployment-time migrations.
- Material movement to a different owner/project, a new project reference, a new service, or a new shared abstraction is approval-relevant architecture drift.

When ownership is ambiguous, inspect `Microservices.Boilerplate.slnx`, the nearest `.csproj`, current production code, and architecture tests before choosing a location.
