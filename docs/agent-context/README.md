# Agent Context Map

This directory is a progressive-disclosure knowledge base for coding agents. `AGENTS.md` stays intentionally small; task-specific skills load only the references required by the current change.

Repository source, project files, executable architecture tests, accepted ADRs, and CI remain authoritative. These notes are navigation and decision aids, not a replacement for reading the affected implementation.

For **where code belongs**, project ownership, solution dependencies, test ownership, and new-service placement, read `project-structure.md`.

For the machine-level rule that binds selected skills/references/examples into the approved plan and reports context drift, read `context-selection-contract.md`.

## Load by change type

| Change | Read first | Canonical evidence |
| --- | --- | --- |
| Unsure which project/folder owns code | `project-structure.md` | `Microservices.Boilerplate.slnx`, owning `.csproj`, nearest production code |
| New project/service/shared dependency | `project-structure.md` | solution/project files + architecture tests + approved requirements |
| New/modified business use case | `architecture/vertical-slice.md` | `CustomerVerticalSliceArchitectureTests.cs` and nearest Customer slice |
| Aggregate/value-object/invariant | `architecture/domain-boundary.md` | `CustomerDomainBoundaryTests.cs`, `CustomerDomainTests.cs` |
| HTTP contract/error behavior | `architecture/api-and-errors.md` | Customer endpoints, `CustomerHttpResults.cs`, `docs/error-handling.md` |
| ETag/idempotency/transaction behavior | `architecture/concurrency-idempotency.md` | AddingAddress/UpdatingAddress handlers and Customer integration tests |
| EF Core/schema/migration | `architecture/persistence.md` | `CustomerDbContext.cs`, Migrator, persistence tests |
| Events/commands/outbox/retry | `architecture/messaging.md` | messaging ADRs, shared messaging abstractions, reliability tests |
| Authentication/authorization/claims | `architecture/security.md` | `docs/keycloak-integration.md`, `Microservices.Security`, security tests |
| Test selection / completion | `architecture/testing.md` | `.github/workflows/dotnet-ci.yml` and affected test projects |

## Core rule

First determine **ownership/placement**, then load the smallest set of behavior references that fully covers the affected architectural boundaries. Do not preload every document for every task.

Examples:

- a Customer validation-only change: confirm the existing Customer slice location, then load `vertical-slice.md` and `testing.md` plus the nearest validator/tests;
- a new Customer mutation: confirm `Customer.Api/Features/Customers/<UseCase>/V1`, then load `vertical-slice.md`, `api-and-errors.md`, `concurrency-idempotency.md`, `domain-boundary.md`, and `testing.md` as actually required;
- a MassTransit/outbox change: confirm ownership in `Microservices.Messaging` or the owning service composition point, then load `messaging.md` and `testing.md`, not Customer HTTP documents;
- a Keycloak/authorization change: distinguish shared security plumbing, service-owned authorization, AppHost development realm, and production `infrastructure/keycloak` ownership using `project-structure.md`; then load `security.md` and any HTTP/testing context actually affected;
- a proposal to move code into `src/Shared`: read `project-structure.md` first and require demonstrated cross-service reuse before planning the move.

## Reference service

`Customer.Api` is the repository's concrete business-service reference. `ServiceTemplate` is infrastructure scaffolding. Derive structural conventions from Customer, but never copy Customer business semantics into another bounded context.

## Evidence priority

When guidance conflicts or appears stale, resolve in this order:

1. acceptance criteria and explicit human-approved scope;
2. executable architecture/security/integration tests;
3. accepted ADRs and service documentation;
4. current production implementation and project files;
5. these agent-context notes;
6. generic framework or architectural convention.

If the first four disagree materially, stop and surface the conflict rather than choosing silently.