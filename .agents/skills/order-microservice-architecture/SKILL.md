---
name: order-microservice-architecture
description: Route a task to the smallest repository-specific architecture context needed for this .NET 10 microservices repository. Use when planning/reviewing cross-cutting work or when the affected architectural boundaries are not yet known. Prefer narrower task skills once the change type is identified.
---

# Architecture Context Router

This skill is a router, not the full architecture manual.

Read `AGENTS.md`, then `docs/agent-context/README.md`. Classify the task by affected boundaries and load only the corresponding references/skills.

## Boundary routing

- business endpoint/use case -> `$implement-vertical-slice`;
- aggregate/value-object/invariant -> `$change-domain-model`;
- EF Core/schema/migration/transaction -> `$change-persistence`;
- event/command/outbox/retry/topology -> `$change-messaging`;
- Keycloak/authentication/authorization/claims -> `$change-security`;
- verification only -> `$verify-dotnet-change`.

Multiple skills may be required for a real feature, but do not load unrelated architecture areas.

Example: a Customer mutation that changes an invariant and EF mapping may require `implement-vertical-slice`, `change-domain-model`, and `change-persistence`; it should not load messaging/security unless the task actually affects them.

## Global decisions

Regardless of selected skill:

- preserve bounded-context ownership;
- prefer executable repository rules and nearest production examples over generic architecture preferences;
- do not invent Order-domain behavior absent from requirements/repository evidence;
- do not introduce horizontal repositories/application services merely to make pure vertical slices look layered;
- architecture/security/integration/shared-platform/destructive migration changes require explicit human review;
- deterministic CI is the final verification authority.

If repository evidence and approved requirements materially conflict, stop and surface the conflict rather than silently choosing one.