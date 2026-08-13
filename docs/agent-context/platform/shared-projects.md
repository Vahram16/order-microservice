# Shared Platform Projects

Use this document when a task changes `src/Shared`, proposes a new shared abstraction, or needs to decide whether behavior is service-local versus cross-service.

The admission rule is strict: **shared code is earned by demonstrated stable cross-service reuse**. Do not move service-local behavior into `src/Shared` because it might be reused later.

## `Microservices.Application`

Purpose: shared application-level contracts and pipeline behavior.

Owns concepts such as:

- `ICommand<TResponse>` / command handlers;
- `IQuery<TResponse>` / query handlers;
- shared validation/pipeline behavior;
- application-facing integration messaging boundaries.

It references `Microservices.Contracts` and uses MediatR/FluentValidation internally.

Do not place service-specific use cases, aggregates, EF code, HTTP results, Keycloak configuration, or RabbitMQ topology here.

## `Microservices.Contracts`

Purpose: framework-free durable contracts that genuinely cross bounded-context/service boundaries.

Its current project file is dependency-free. Preserve that property unless an explicit architecture decision changes it.

Put here only stable integration contracts that cross services. Do not put internal CQRS messages, HTTP DTOs, service entities, persistence models, transport headers, or speculative contracts here.

Contract shape is a compatibility surface; changes require explicit compatibility review.

## `Microservices.Messaging`

Purpose: MassTransit/RabbitMQ transport implementation and EF inbox/outbox infrastructure behind application-facing abstractions.

Current shared dependencies:

```text
Microservices.Messaging
├── Microservices.Application
└── Microservices.Contracts
```

Owns:

- MassTransit/RabbitMQ registration/configuration;
- bus outbox / consumer inbox-outbox infrastructure;
- retry/redelivery/failure routing/topology implementation;
- command routing infrastructure;
- messaging telemetry/infrastructure behavior.

Do not expose MassTransit/RabbitMQ types into service application/domain behavior. Load `../architecture/messaging.md` for messaging semantics.

## `Microservices.Persistence.Postgres`

Purpose: reusable PostgreSQL/Npgsql/EF-provider-specific infrastructure.

It currently depends on the Npgsql EF Core provider and has no shared-project references.

Put only reusable provider-specific behavior here. Service-owned schemas, entity configurations, migrations, constraints, queries, and business consistency behavior stay in the owning service.

## `Microservices.Primitives`

Purpose: framework-free low-level operation result/error primitives shared across bounded contexts.

The project is deliberately dependency-free. Preserve that property unless an explicit architecture decision changes it.

Do not add ASP.NET Core, EF Core, MediatR, transport, security, or service-specific semantics.

## `Microservices.Security`

Purpose: reusable resource-API authentication/token-validation and authorization-policy plumbing.

It uses ASP.NET Core/JWT bearer support.

Owns:

- shared bearer-token validation infrastructure;
- reusable scope/role policy mechanics;
- cross-service security configuration primitives.

Bounded-context resource ownership and business/domain authorization remain service-owned. Load `../architecture/security.md` when changing this boundary.

## `Microservices.ServiceDefaults`

Purpose: cross-service framework/platform defaults.

Owns shared concerns such as:

- OpenTelemetry/observability;
- health checks;
- service discovery and resilience defaults;
- OpenAPI/Scalar defaults;
- intentionally centralized framework-level validation/error/status-code behavior.

Do not move service-specific behavior here merely because multiple APIs use ASP.NET Core.

## Dependency model

Use this only as a responsibility model, then verify exact references in the affected `.csproj` files:

```text
Microservices.Contracts
        ▲
        │
Microservices.Application
        ▲
        │
Microservices.Messaging

Microservices.Primitives       Microservices.Persistence.Postgres
        ▲                                  ▲
        └──────── service APIs ─────────────┘
                         ▲
             Security / ServiceDefaults
```

Rules:

- shared libraries must not reference concrete service projects;
- service domain source must not inherit infrastructure dependencies merely because its containing API project references them;
- Contracts and Primitives stay at the dependency-light end of the graph;
- transport implementation belongs in Messaging, not Application/Contracts;
- reusable provider-specific persistence belongs in Persistence.Postgres, while schema/model ownership remains with the service;
- every new project reference or shared abstraction is architecture-sensitive and must be explicit in the approved plan.

## Shared-admission test

Before placing new code in `src/Shared`, answer all of these:

1. Which two or more real services need the same stable responsibility now?
2. Is the abstraction semantically identical across those services rather than merely syntactically similar?
3. Can it remain free of one service's domain vocabulary/data ownership?
4. Does the proposed dependency direction preserve existing architecture boundaries?
5. Is the migration/compatibility cost of centralizing lower than keeping behavior service-local?

If those answers are not supported by repository evidence and approved requirements, keep the behavior in the owning service.