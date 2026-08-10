---
name: order-microservice-architecture
description: Route a task to the owning project/folder and the smallest repository-specific architecture context needed for this .NET 10 microservices repository. Use when planning/reviewing cross-cutting work, code placement is unclear, or affected architectural boundaries are not yet known. Prefer narrower task skills once ownership and change type are identified.
---

# Architecture and Project Context Router

This skill is a router, not the full architecture manual.

## Routing sequence

1. Read `AGENTS.md`.
2. Read `docs/agent-context/project-structure.md` to identify bounded-context ownership, target project/folder, direct project dependencies, test ownership, and whether a proposed shared location is justified.
3. Read `docs/agent-context/README.md` to select only the behavior-specific references needed.
4. Inspect the owning `.csproj`, nearest canonical production example, and its tests.
5. Load the narrowest matching focused skill(s).

Do not design a new file/project before ownership and placement are explicit.

## Boundary routing

- business endpoint/use case -> `$implement-vertical-slice`;
- aggregate/value-object/invariant -> `$change-domain-model`;
- EF Core/schema/migration/transaction -> `$change-persistence`;
- event/command/outbox/retry/topology -> `$change-messaging`;
- Keycloak/authentication/authorization/claims -> `$change-security`;
- verification only -> `$verify-dotnet-change`.

Multiple skills may be required for a real feature, but do not load unrelated architecture areas.

Example: a Customer mutation that changes an invariant and EF mapping may require `implement-vertical-slice`, `change-domain-model`, and `change-persistence`; its owner still remains Customer unless requirements prove another bounded context is involved. It should not load messaging/security merely because those shared projects exist.

## Placement decisions

Before proposing code in `src/Shared`, a new service, AppHost, infrastructure, or a Migrator, verify the ownership rules in `project-structure.md`.

Material placement changes are architecture changes. Examples:

- a slice-local change unexpectedly needs a shared project abstraction;
- a Customer change actually requires a new bounded context;
- API startup would need to run migrations instead of the Migrator;
- service business authorization is being moved into `Microservices.Security`;
- application behavior would need direct MassTransit/RabbitMQ types.

These require explicit replanning/review rather than silent relocation.

## Global decisions

Regardless of selected skill:

- preserve bounded-context ownership;
- prefer executable repository rules, project files, and nearest production examples over generic architecture preferences;
- do not invent Order-domain behavior absent from requirements/repository evidence;
- do not introduce horizontal repositories/application services merely to make pure vertical slices look layered;
- do not move service-local behavior into shared projects without demonstrated cross-service reuse;
- architecture/security/integration/shared-platform/destructive migration changes require explicit human review;
- deterministic CI is the final verification authority.

If repository evidence and approved requirements materially conflict, stop and surface the conflict rather than silently choosing one.