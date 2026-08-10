# Project Structure and Code Placement

This file is the repository placement map for coding agents. Use it when deciding **where a change belongs**, which project owns it, which neighboring code is canonical, and which dependency directions must be preserved.

Do not treat this as a substitute for inspecting current source. The solution file, project files, architecture tests, accepted ADRs, and affected production code remain authoritative.

## Solution shape

The solution is `Microservices.Boilerplate.slnx` and currently contains this project structure:

```text
src/
├── AppHost/
│   └── Microservices.AppHost/
│       └── Microservices.AppHost.csproj
│
├── Services/
│   ├── Customer/
│   │   ├── Customer.Api/
│   │   │   ├── Domain/
│   │   │   ├── Features/
│   │   │   ├── Infrastructure/
│   │   │   ├── Persistence/
│   │   │   ├── Program.cs
│   │   │   └── Customer.Api.csproj
│   │   └── Customer.Migrator/
│   │       └── Customer.Migrator.csproj
│   │
│   └── ServiceTemplate/
│       ├── ServiceTemplate.Api/
│       │   └── ServiceTemplate.Api.csproj
│       └── ServiceTemplate.Migrator/
│           └── ServiceTemplate.Migrator.csproj
│
└── Shared/
    ├── Microservices.Application/
    ├── Microservices.Contracts/
    ├── Microservices.Messaging/
    ├── Microservices.Persistence.Postgres/
    ├── Microservices.Primitives/
    ├── Microservices.Security/
    └── Microservices.ServiceDefaults/

tests/
├── Customer.Api.Tests/
├── Microservices.Application.Tests/
├── Microservices.ArchitectureTests/
├── Microservices.Messaging.Tests/
├── Microservices.Primitives.Tests/
├── Microservices.Security.Tests/
└── Microservices.ServiceDefaults.Tests/

infrastructure/
├── keycloak/
├── rabbitmq/
└── observability/

scripts/
└── repository/development verification scripts

docs/
├── adr/
├── customer-service.md
├── error-handling.md
├── keycloak-integration.md
├── messaging-failure-delivery-policy.md
├── agentic-automation.md
└── agent-context/

.agents/
└── skills/

.automation/
├── config.example.json
└── schemas/

.codex/
└── config.toml
```

The repository name does **not** imply that an `Order` service/domain already exists. Do not create an Order aggregate, service, API, database, contracts, scopes, or events unless an approved task defines that business boundary.

## Project responsibilities

### `src/AppHost/Microservices.AppHost`

**Purpose:** local .NET Aspire orchestration only.

Owns:

- local PostgreSQL resources/databases;
- local RabbitMQ resource wiring;
- local Keycloak resource and development realm import;
- startup ordering/dependencies between local resources, service Migrators, and APIs;
- development-only user secrets used by the AppHost.

It references the Customer and ServiceTemplate API/Migrator projects so Aspire can orchestrate them locally.

Do not put business logic, domain logic, API behavior, reusable security primitives, messaging abstractions, or production deployment logic here. Production must not depend on the AppHost existing.

Use this project when a task changes **local development orchestration**, resource startup, local dependency wiring, or the development Keycloak import path.

### `src/Services/Customer/Customer.Api`

**Purpose:** the concrete business-owned Customer bounded context and the repository's canonical business-service implementation.

This is a single service project with source boundaries inside the project rather than separate Domain/Application/Infrastructure assemblies.

Internal structure:

```text
Customer.Api/
├── Domain/          business model, invariants, value objects, aggregate behavior
├── Features/        versioned pure vertical slices and stable cross-slice Customer helpers
├── Infrastructure/  Customer service infrastructure adapters/composition helpers
├── Persistence/     CustomerDbContext, EF mappings, migrations, DB-specific persistence helpers
├── Program.cs       service composition root
└── appsettings*.json
```

Direct shared-project dependencies currently include:

```text
Customer.Api
├── Microservices.Application
├── Microservices.Persistence.Postgres
├── Microservices.Primitives
├── Microservices.Security
└── Microservices.ServiceDefaults
```

Do not add a direct dependency merely because another shared project is convenient. First prove that the affected Customer behavior belongs behind that shared boundary.

#### `Customer.Api/Domain`

Put here:

- aggregates/entities that express Customer-owned business state;
- value objects;
- domain invariants and state transitions;
- domain-semantic errors/outcomes;
- failure-atomic business behavior.

Do not put here:

- ASP.NET Core/HTTP concepts;
- MediatR/CQRS dispatching;
- FluentValidation;
- EF Core/Npgsql/database terminology;
- authentication claims/header parsing;
- API idempotency terminology;
- MassTransit/RabbitMQ;
- feature, persistence, infrastructure, or shared service plumbing.

Executable boundary: `tests/Customer.Api.Tests/CustomerDomainBoundaryTests.cs`.

#### `Customer.Api/Features`

Put **business use cases** here as pure versioned vertical slices.

Canonical shape:

```text
Features/
└── Customers/
    ├── Common/
    └── <UseCase>/
        └── V1/
            ├── <UseCase>Endpoint.cs
            ├── <UseCase>Request.cs        when needed
            ├── <UseCase>Command.cs        for mutations
            ├── <UseCase>Query.cs          for reads
            ├── <UseCase>Validator.cs
            ├── <UseCase>Handler.cs
            ├── <UseCase>Result.cs         when needed
            └── <UseCase>Response.cs       when operation-specific
```

A slice owns its own HTTP mapping, request contract, command/query, validation, handler, authorization behavior, and operation-specific response behavior.

Do not create a horizontal `Services/`, `Managers/`, generic `Repositories/`, or shared application-service layer merely to move logic out of a slice.

Do not reference sibling use-case namespaces from another slice. Promote only genuinely stable cross-slice primitives to `Features/Customers/Common`.

Canonical examples:

- mutation endpoint: `Features/Customers/UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs`;
- mutation with idempotency/transaction/race handling: `Features/Customers/AddingAddress/V1/AddCustomerAddressHandler.cs`;
- query/read slice: `Features/Customers/GettingCurrent/V1/`;
- destructive lifecycle operation: `Features/Customers/ClosingAccount/V1/`;
- architecture enforcement: `tests/Customer.Api.Tests/CustomerVerticalSliceArchitectureTests.cs`.

#### `Customer.Api/Persistence`

Put here:

- `CustomerDbContext`;
- entity type configurations;
- Customer-owned PostgreSQL constraints/index definitions;
- Customer EF migrations;
- persistence-only query/update helpers;
- known provider-specific persistence translation where it is Customer-owned.

The service owns its schema. Do not move business invariants into EF configuration merely because a database constraint also defends them. Use both domain enforcement and database constraints where the repository's consistency model requires defense in depth.

Canonical entry point: `Persistence/CustomerDbContext.cs`.

#### `Customer.Api/Infrastructure`

Put Customer-specific adapters/infrastructure helpers here when they do not belong to a single slice and are not general enough for `src/Shared`.

Do not use `Infrastructure` as a miscellaneous folder. A helper used only by one feature should normally remain in that slice unless there is a stable service-wide responsibility.

#### `Customer.Api/Program.cs`

This is the Customer service composition root. Put DI/service registration and endpoint-group composition here or behind explicit registration extensions used by the composition root.

Do not put business behavior in `Program.cs`.

### `src/Services/Customer/Customer.Migrator`

**Purpose:** run-once deployment migration process for the Customer database.

It references `Customer.Api` to use the service-owned EF model/migrations, plus PostgreSQL and ServiceDefaults support.

Use it for deployment-time migration execution behavior. The Customer API process must not apply schema migrations during startup.

Deployment order is:

```text
Customer.Migrator completes successfully
        ↓
Customer.Api replicas start/roll
```

### `src/Services/ServiceTemplate/ServiceTemplate.Api`

**Purpose:** infrastructure/service scaffolding, not the canonical source of Customer business semantics and not an existing Order domain.

It demonstrates how a new service composes shared platform capabilities. Its direct shared-project references currently include:

```text
ServiceTemplate.Api
├── Microservices.Application
├── Microservices.Contracts
├── Microservices.Messaging
├── Microservices.Persistence.Postgres
├── Microservices.Security
└── Microservices.ServiceDefaults
```

Use Customer to learn how a real business bounded context structures vertical slices and domain behavior. Use ServiceTemplate to learn how a service wires generic platform/messaging infrastructure.

When creating a future real bounded context from an approved task, copy **structural platform conventions**, not placeholder/example business rules.

### `src/Services/ServiceTemplate/ServiceTemplate.Migrator`

**Purpose:** run-once migration process paired with ServiceTemplate.Api.

It references its API project plus PostgreSQL and ServiceDefaults support. The same Migrator-before-API rule applies.

## Shared projects

Shared projects exist only for stable cross-service concerns. A new shared abstraction requires demonstrated multi-service value; do not move service-local behavior into `src/Shared` preemptively.

### `src/Shared/Microservices.Application`

**Purpose:** shared application-level contracts and pipeline behavior.

Currently owns concepts such as:

- `ICommand<TResponse>` / `ICommandHandler<...>`;
- `IQuery<TResponse>` / `IQueryHandler<...>`;
- shared validation/pipeline behavior;
- application-facing integration messaging boundaries such as event publication and typed command sending.

It references `Microservices.Contracts` and uses MediatR/FluentValidation internally.

Do not place service-specific use cases, aggregates, EF code, HTTP results, Keycloak configuration, or RabbitMQ topology here.

### `src/Shared/Microservices.Contracts`

**Purpose:** framework-free integration contracts shared across service boundaries.

This project intentionally has no package/project dependencies in its current project file.

Put here only durable cross-service message contracts that truly cross bounded contexts. Do not put internal commands/queries, HTTP DTOs, service entities, persistence models, or transport headers here.

Integration contracts are compatibility surfaces; changes require explicit compatibility review.

### `src/Shared/Microservices.Messaging`

**Purpose:** MassTransit/RabbitMQ transport implementation and EF inbox/outbox infrastructure.

It depends on `Microservices.Application` and `Microservices.Contracts` and contains the transport implementation behind their application-facing abstractions.

Put here:

- MassTransit/RabbitMQ registration and configuration;
- bus outbox / consumer inbox-outbox infrastructure;
- retry/redelivery/topology implementation;
- command routing implementation;
- messaging telemetry/infrastructure behavior.

Do not expose MassTransit/RabbitMQ types into service domain/application behavior. Application code publishes/sends through the approved abstractions.

### `src/Shared/Microservices.Persistence.Postgres`

**Purpose:** reusable PostgreSQL/EF-provider-specific infrastructure.

It currently depends on the Npgsql EF Core provider and has no shared-project references.

Put only reusable provider-specific behavior here. Service-owned schemas, entity configurations, migrations, constraints, and business query behavior stay in the owning service.

### `src/Shared/Microservices.Primitives`

**Purpose:** framework-free operation result/error primitives shared across bounded contexts.

Its project is deliberately dependency-free. Preserve that property unless an explicit architecture decision changes it.

Put here only genuinely framework-free low-level primitives such as result/error contracts. Do not add ASP.NET Core, EF Core, MediatR, transport, security, or service-specific semantics.

### `src/Shared/Microservices.Security`

**Purpose:** reusable resource-API authentication/token-validation and authorization-policy plumbing.

It uses ASP.NET Core and JWT bearer support.

Put here:

- shared bearer-token validation infrastructure;
- reusable scope/role policy mechanics;
- cross-service security configuration primitives.

Do not put a bounded context's resource-ownership or business authorization decisions here. Those belong in the owning service because they require domain context.

### `src/Shared/Microservices.ServiceDefaults`

**Purpose:** shared API/platform defaults.

Owns cross-service plumbing such as:

- OpenTelemetry/observability;
- health checks;
- service discovery/resilience defaults;
- OpenAPI/Scalar defaults;
- shared framework-level error/status-code/validation pipeline behavior where intentionally centralized.

Do not move service-specific behavior here just because multiple APIs use ASP.NET Core.

## Dependency direction

Use this as a mental model, then verify exact references in the affected `.csproj` files:

```text
                     Microservices.Contracts
                              ▲
                              │
                  Microservices.Application
                              ▲
                              │
                  Microservices.Messaging

Microservices.Primitives            Microservices.Persistence.Postgres
         ▲                                      ▲
         │                                      │
         └──────────── service APIs ─────────────┘
                         ▲
                         │
        Microservices.Security / ServiceDefaults
                         ▲
                         │
                  service composition
```

Important nuance: this is a responsibility/direction model, not a claim that every service references every shared project. Inspect the owning service's `.csproj` before adding a dependency.

Rules:

- service domain code must not depend on shared infrastructure merely because the service project itself references it;
- shared libraries must not reference concrete service projects;
- `Microservices.Contracts` and `Microservices.Primitives` should remain at the most dependency-light end of the graph;
- transport implementation belongs in Messaging, not Application/Contracts;
- provider-specific reusable persistence belongs in Persistence.Postgres, while service schema/model ownership stays in the service;
- cross-service sharing is earned through demonstrated reuse, not predicted reuse.

## Tests and where verification belongs

### `tests/Customer.Api.Tests`

Owns Customer domain, vertical-slice architecture, API integration, persistence/concurrency/idempotency, error behavior, and Customer service-specific verification.

When modifying Customer behavior, this is normally the first test project to inspect.

### `tests/Microservices.Application.Tests`

Owns shared application/CQRS/pipeline/application-boundary verification.

### `tests/Microservices.ArchitectureTests`

Owns repository-wide production dependency and architectural boundary checks. Treat failures as architecture evidence, not obstacles to bypass.

### `tests/Microservices.Messaging.Tests`

Owns messaging architecture and real RabbitMQ/PostgreSQL reliability behavior, including outbox/inbox, routing, retries/redelivery, failure queues, duplicate suppression, recovery, and topology guarantees.

### `tests/Microservices.Primitives.Tests`

Owns `Result`, `Result<T>`, `OperationError`, metadata, and other framework-free primitive invariants.

### `tests/Microservices.Security.Tests`

Owns shared token-validation and authorization-policy security behavior.

### `tests/Microservices.ServiceDefaults.Tests`

Owns shared service-default framework behavior such as common validation/error/status-code pipeline behavior.

The final authoritative PR verification remains `.github/workflows/dotnet-ci.yml`.

## Infrastructure and operations folders

### `infrastructure/keycloak`

Production Keycloak image/build assets. Do not confuse this with service authorization logic. Keycloak owns identity-provider concerns; APIs own resource/domain authorization.

### `infrastructure/rabbitmq`

Pinned RabbitMQ image/plugin configuration used by development/CI/production baseline. Changes can alter broker capabilities/topology and require messaging reliability verification.

### `infrastructure/observability`

Deployable Grafana/Prometheus messaging observability assets. Keep metric names/semantics aligned with the messaging implementation and tests.

### `scripts`

Repository verification/development scripts. Prefer deterministic scripts for repeatable environment checks instead of embedding long shell behavior into agent prompts.

## Agent automation folders

### `.agents/skills`

Task-specific Codex skills. Skills define **how to perform a kind of work**, not business truth. Keep each skill focused and route to repository references instead of copying the whole architecture into every skill.

### `.automation`

Machine-readable orchestration contracts:

- `schemas/plan.schema.json` — planning/approval contract;
- `schemas/execution-result.schema.json` — execution evidence contract;
- `config.example.json` — external orchestrator policy/configuration template.

Do not store runtime secrets, Jira tokens, GitHub App private keys, OpenAI credentials, or mutable run state in committed repository files.

### `.codex/config.toml`

Repository-scoped Codex/MCP configuration. It declares integration endpoints/policy, not credentials.

### `docs/agent-context`

Progressive-disclosure project knowledge for agents. This `project-structure.md` answers ownership/placement. Files under `architecture/` answer behavior/boundary rules. `context-selection-contract.md` controls how planning selects the smallest approved context package.

## Root engineering files

- `Microservices.Boilerplate.slnx` — authoritative solution project membership/grouping.
- `Directory.Build.props` — cross-project build policy: .NET target, nullable, analyzers, warnings-as-errors, deterministic builds.
- `Directory.Packages.props` — central package version management.
- `global.json` — SDK selection.
- `.editorconfig` — repository code/style analyzer configuration.
- `.github/workflows/dotnet-ci.yml` — authoritative pull-request CI gate.

Agents must inspect these before proposing SDK/package/build-policy changes.

## Code placement decision table

Use this before creating a file or project:

| Change | Primary location | Do not default to |
| --- | --- | --- |
| New Customer API use case | `Customer.Api/Features/Customers/<UseCase>/V1` | shared service/repository layer |
| Customer business invariant/value object | `Customer.Api/Domain` | handler, EF configuration, HTTP layer |
| Customer HTTP mapping/Problem Details | owning slice or stable `Features/Customers/Common` | Domain |
| Customer EF mapping/schema/migration | `Customer.Api/Persistence` | Shared persistence project |
| Customer-only infrastructure helper | `Customer.Api/Infrastructure` or owning slice | `src/Shared` without reuse proof |
| New service migration runner behavior | owning `<Service>.Migrator` | API startup |
| Cross-service CQRS/application boundary | `Microservices.Application` | concrete service |
| Durable cross-service message contract | `Microservices.Contracts` | service-internal command/query |
| MassTransit/RabbitMQ/outbox implementation | `Microservices.Messaging` | service Domain/Application |
| Reusable Npgsql/EF provider helper | `Microservices.Persistence.Postgres` | service domain |
| Framework-free result/error primitive | `Microservices.Primitives` | ServiceDefaults/Application |
| Shared JWT/scope/role policy plumbing | `Microservices.Security` | domain model |
| Shared observability/health/OpenAPI defaults | `Microservices.ServiceDefaults` | business slice |
| Local development resource orchestration | `Microservices.AppHost` | production service code |
| Broker/IdP/observability deployment asset | `infrastructure/*` | application/domain source |
| Agent workflow instructions | `.agents/skills/*` | production C# source |
| Agent architecture knowledge | `docs/agent-context/*` | giant `AGENTS.md` |
| Deterministic automation schema/policy | `.automation/*` | skill prose only |

## New bounded context/service placement

When an approved Jira task genuinely introduces a new bounded context, the default structural shape is:

```text
src/Services/<BoundedContext>/
├── <BoundedContext>.Api/
│   ├── Domain/             if the bounded context has a business domain model
│   ├── Features/
│   ├── Infrastructure/     only for stable service-wide adapters
│   ├── Persistence/        if the service owns persisted state
│   ├── Program.cs
│   └── <BoundedContext>.Api.csproj
└── <BoundedContext>.Migrator/
    └── <BoundedContext>.Migrator.csproj
```

Then add the corresponding service test project under `tests/` and wire local orchestration in AppHost if required.

This is a **structural default only**. The approved task must still define the real domain, persistence needs, contracts, authorization, messaging, and API behavior. Never infer them from the repository name or ServiceTemplate examples.

## Placement protocol for agents

Before proposing a new file/project:

1. identify the bounded context/owner;
2. locate the nearest analogous production implementation;
3. inspect this structure map plus the relevant focused architecture reference;
4. inspect the owning `.csproj` and architecture tests;
5. place behavior in the narrowest owning boundary;
6. avoid `src/Shared` unless reuse already exists or the approved plan explicitly justifies a new shared contract;
7. include the selected target project/folder and reason in `plan.json`;
8. if implementation discovers the planned owner/location is wrong in a material way, return `replan_required` instead of moving code across architectural boundaries silently.
