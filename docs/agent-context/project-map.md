# Project Ownership Map

Use this file as the **small, first-hop router** for repository ownership. It answers only: _which component owns this change, and which scoped context should be loaded next?_

Do not load every service/platform document. After ownership is clear, load only the matching scoped context plus behavior-specific architecture references.

## Solution ownership

| Change area | Owner | Load next |
| --- | --- | --- |
| Customer business behavior, identity link, Customer persistence | `src/Services/Customer` | `services/customer.md` |
| Inventory stock and order-reservation lifecycle | `src/Services/Inventory` | `services/inventory.md` |
| Order lifecycle, checkout orchestration, order snapshots | `src/Services/Order` | `services/order.md` |
| Payment methods, order-payment execution, provider integration | `src/Services/Payment` | `services/payment.md` |
| Product catalog behavior, Product domain, catalog change publication | `src/Services/Product` | `services/product.md` |
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

src/Services/Inventory/
├── Inventory.Api
└── Inventory.Migrator

src/Services/Order/
├── Order.Api
└── Order.Migrator

src/Services/Payment/
├── Payment.Api
└── Payment.Migrator

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
├── Inventory.Api.Tests
├── Microservices.Application.Tests
├── Microservices.ArchitectureTests
├── Microservices.Messaging.Tests
├── Microservices.Primitives.Tests
├── Microservices.Security.Tests
├── Microservices.ServiceDefaults.Tests
├── Order.Api.Tests
├── Payment.Api.Tests
└── Product.Api.Tests
```

## Routing rules

- Customer work -> `services/customer.md`.
- Inventory work -> `services/inventory.md`.
- Order work -> `services/order.md`.
- Payment work -> `services/payment.md`.
- Product work -> `services/product.md`.
- Generic service scaffolding -> `services/service-template.md`.
- Shared-project changes -> `platform/shared-projects.md`; treat new cross-service abstractions as architecture-sensitive.
- Local Aspire changes -> `platform/apphost.md`.
- Broker/identity/observability deployment assets -> `platform/infrastructure.md`.
- Verification ownership -> `testing-map.md`.
- Inspect the owning `.csproj` before any project/package reference change.

## Hard boundaries

- A business service owns its domain, API behavior, persistence model/schema, and migrations.
- Order owns order state and distributed checkout decisions; it does not own stock or provider payment state.
- Inventory owns stock and reservations; Product owns catalog identity/description/price state.
- Payment owns reusable methods, PaymentIntent/provider execution, 3-D Secure reconciliation, and provider identifiers.
- Cross-service workflow contracts are transport-independent and live in `Microservices.Contracts`; MassTransit/RabbitMQ stay infrastructure concerns.
- `src/Shared` is for demonstrated stable cross-service concerns, not speculative reuse.
- AppHost is local development orchestration, not production business logic.
- API processes do not apply migrations at startup; service Migrators execute deployment-time migrations.
- New bounded contexts, project references, durable contracts, or security capabilities require explicit approved requirements. Order and Inventory exist because the checkout workflow was explicitly approved; repository naming alone remains insufficient justification for future services.

When ownership is ambiguous, inspect `Microservices.Boilerplate.slnx`, the nearest `.csproj`, current production code, and architecture tests before choosing a location.
