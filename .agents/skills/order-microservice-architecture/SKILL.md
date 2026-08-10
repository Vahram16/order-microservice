---
name: order-microservice-architecture
description: Route a task to the owning service/platform area and the smallest repository-specific architecture context needed for this .NET 10 microservices repository. Use when ownership, code placement, or affected architectural boundaries are unclear. Prefer narrower task skills once owner and change type are known.
---

# Architecture Context Router

This skill is a router, not an architecture manual.

## Routing sequence

1. Read `AGENTS.md`.
2. Read `docs/agent-context/project-map.md` to identify the owning area.
3. Load exactly one matching scoped owner document when ownership details are needed:
   - Customer -> `docs/agent-context/services/customer.md`;
   - ServiceTemplate/new-service scaffolding -> `docs/agent-context/services/service-template.md`;
   - shared libraries -> `docs/agent-context/platform/shared-projects.md`;
   - AppHost -> `docs/agent-context/platform/apphost.md`;
   - production infrastructure assets -> `docs/agent-context/platform/infrastructure.md`.
4. Read `docs/agent-context/README.md` to select only behavior-specific architecture references.
5. Inspect the owning `.csproj`, nearest canonical production example, and relevant tests.
6. Load the narrowest matching focused skill(s).

Do not load every owner document and do not design new files/projects before ownership is explicit.

## Boundary routing

- business endpoint/use case -> `$implement-vertical-slice`;
- aggregate/value-object/invariant -> `$change-domain-model`;
- EF Core/schema/migration/transaction -> `$change-persistence`;
- event/command/outbox/retry/topology -> `$change-messaging`;
- Keycloak/authentication/authorization/claims -> `$change-security`;
- verification only -> `$verify-dotnet-change`.

Multiple focused skills may be required for a real feature, but unrelated architecture areas should remain unloaded.

Example: a Customer mutation changing an invariant and EF mapping may require `implement-vertical-slice`, `change-domain-model`, and `change-persistence`; ownership remains Customer unless requirements prove another bounded context is involved. Messaging/security stay unloaded unless actually affected.

## Placement-sensitive changes

Load the relevant owner context and require explicit architecture review before:

- moving service-local behavior into `src/Shared`;
- adding/removing project references;
- creating a new service/bounded context/project;
- moving migration execution into API startup;
- moving service/domain authorization into shared security plumbing;
- exposing MassTransit/RabbitMQ types to application/domain code;
- changing AppHost or production infrastructure as part of a feature.

These are approval-relevant placement/dependency changes, not ordinary implementation details.

## Global decisions

- preserve bounded-context ownership;
- prefer executable repository rules, project files, and nearest production examples over generic architecture preferences;
- do not invent Order-domain behavior absent from approved requirements/repository evidence;
- do not introduce horizontal repositories/application services merely to make pure vertical slices look layered;
- shared code requires demonstrated stable cross-service reuse;
- architecture/security/integration/shared-platform/destructive migration changes require explicit human review;
- deterministic CI is final verification authority.

If repository evidence and approved requirements materially conflict, stop and surface the conflict rather than choosing silently.