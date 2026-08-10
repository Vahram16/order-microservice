---
name: order-microservice-architecture
description: Apply the repository's enforced .NET 10 microservice, pure vertical-slice, domain, persistence, messaging, security, error-handling, and testing architecture. Use when planning, implementing, or reviewing changes in this repository, especially Customer-style business services or shared microservice infrastructure.
---

# Order Microservice Architecture

Use repository evidence rather than generic architecture preferences. Start with `AGENTS.md`, then load only the references relevant to the requested change.

## Context map

- Business service or new vertical slice: `docs/customer-service.md`, the nearest existing slice, `tests/Customer.Api.Tests/CustomerVerticalSliceArchitectureTests.cs`, and `CustomerDomainBoundaryTests.cs`.
- HTTP/error behavior: `docs/error-handling.md` and existing Customer error/result mapping.
- Authentication/authorization/identity: `docs/keycloak-integration.md`, `Microservices.Security`, and relevant security tests.
- Messaging/outbox/inbox/contracts: accepted `docs/adr/0001-*` through `0005-*`, `docs/messaging-failure-delivery-policy.md`, shared messaging code, and architecture/reliability tests.
- Persistence/migrations: the owning service `DbContext`, its migrations, its Migrator project, and persistence tests.
- Platform/API defaults: `Microservices.ServiceDefaults` and its tests.
- CI/release validation: `.github/workflows/dotnet-ci.yml`.

## Decision rules

1. Preserve bounded-context ownership. Do not make another service's database, aggregate, or persistence model a local implementation detail.
2. Prefer a new or changed business behavior inside its owning versioned vertical slice. Do not create a horizontal application-service or repository layer just to make the code look layered.
3. Keep domain behavior framework-free and failure-atomic. Domain code owns invariants; it does not know HTTP, EF Core, MediatR, authentication headers, persistence terminology, or infrastructure.
4. Use shared CQRS abstractions (`ICommand`, `IQuery`, matching handlers), not raw MediatR request contracts in business slices.
5. Keep sibling slices independent. Promote code to `Common` only when it is genuinely stable and reused across slices.
6. Preserve explicit idempotency, optimistic concurrency, and transaction semantics demonstrated by Customer handlers. Do not replace them with broad retries or hidden middleware semantics without repository evidence.
7. Keep semantic error translation layered: domain outcome -> application meaning -> presentation Problem Details.
8. Keep transport details behind approved messaging boundaries. Application/domain code must not depend on MassTransit/RabbitMQ.
9. Keep identity-provider responsibilities in Keycloak and resource/domain authorization in the API. Least privilege is the default.
10. Migrations are deployment work executed by a Migrator; APIs do not migrate themselves.

## New business service guidance

`ServiceTemplate` is infrastructure scaffolding, while `Customer` is the concrete reference for a business-owned vertical-slice service. When a Jira task asks for a new domain, derive only structural conventions from Customer. Never copy Customer business rules into another bounded context and never invent missing Order-domain requirements.

## Architecture-impact classification

Classify a proposed change as one or more of:

- `slice-local`: isolated endpoint/handler/domain behavior with no durable cross-boundary change;
- `domain-contract`: aggregate/value-object/invariant change;
- `persistence-contract`: schema, concurrency, uniqueness, migration, or transaction change;
- `integration-contract`: published/sent message shape or routing change;
- `security-boundary`: authentication, authorization, claims, scope/role, client, or identity change;
- `shared-platform`: shared library/default behavior affecting multiple services;
- `deployment`: AppHost, container, migration ordering, observability, or CI behavior.

Anything in `integration-contract`, `security-boundary`, `shared-platform`, or a destructive/breaking `persistence-contract` is at least high-risk for autonomous execution and requires explicit human review.

## Verification selection

Choose verification from actual affected boundaries:

- Slice/domain change -> Customer domain/validation/integration/vertical-slice architecture tests as applicable.
- Shared application/primitives -> their dedicated unit tests plus architecture tests.
- Messaging -> messaging architecture plus real RabbitMQ/PostgreSQL reliability tests.
- Security/Keycloak -> security tests plus the development realm smoke verification when configuration changes.
- Service defaults -> service-default tests.
- Persistence schema -> owning service tests plus migration review and CI.

Do not weaken or delete an architecture test merely because new code violates it. Treat such a failure as evidence that either the change is wrong or the architectural decision needs explicit human approval and documentation.