# Agent Context Map

This directory is a progressive-disclosure knowledge base for coding agents. `AGENTS.md` stays intentionally small; owner context, architecture references, and workflow skills load only when relevant.

Repository source, project files, executable tests, accepted ADRs, and CI remain authoritative. These notes are navigation/decision aids, not substitutes for reading affected implementation.

## First: identify ownership

Start with `project-map.md`. Then load **one matching owner document**, not the whole repository map:

- Customer -> `services/customer.md`;
- ServiceTemplate / approved new-service scaffolding -> `services/service-template.md`;
- shared libraries / proposed shared abstractions -> `platform/shared-projects.md`;
- local Aspire orchestration -> `platform/apphost.md`;
- Keycloak/RabbitMQ/observability deployment assets -> `platform/infrastructure.md`;
- test-project ownership -> `testing-map.md`.

For the machine rule binding placement/context into approved plans, read `context-selection-contract.md` only in planning/orchestration work.

## Then: load behavior-specific architecture

| Change | Architecture reference | Canonical evidence |
| --- | --- | --- |
| New/modified business use case | `architecture/vertical-slice.md` | nearest Customer slice + `CustomerVerticalSliceArchitectureTests.cs` |
| Aggregate/value-object/invariant | `architecture/domain-boundary.md` | Customer domain + domain boundary/tests |
| HTTP contract/error behavior | `architecture/api-and-errors.md` | Customer endpoints, HTTP result mapping, `docs/error-handling.md` |
| ETag/idempotency/transaction/races | `architecture/concurrency-idempotency.md` | AddingAddress/UpdatingAddress + integration tests |
| EF Core/schema/migration | `architecture/persistence.md` | owning DbContext/Migrator/persistence tests |
| Events/commands/outbox/retry/topology | `architecture/messaging.md` | messaging ADRs/implementation/reliability tests |
| Authentication/authorization/claims | `architecture/security.md` | `docs/keycloak-integration.md`, security implementation/tests |
| Detailed verification strategy | `architecture/testing.md` | CI workflow + test ownership map |

## Core rule

Determine **owner first**, then load the smallest set of architecture references needed for the affected boundaries. Prefer 1-3 nearest canonical source/test examples over broad repository exploration.

Examples:

- Customer validator-only change -> `project-map.md` → `services/customer.md` → `vertical-slice.md` → nearest validator/tests;
- Customer mutation changing an invariant -> add `domain-boundary.md`; add concurrency/persistence only if actually affected;
- shared messaging infrastructure -> `project-map.md` → `platform/shared-projects.md` → `messaging.md`; do not load Customer HTTP/domain context;
- production Keycloak asset change -> `platform/infrastructure.md` + `security.md`; AppHost context only if local development orchestration also changes;
- proposal to move service-local code to `src/Shared` -> `platform/shared-projects.md` and explicit architecture review before implementation.

## Reference service

`Customer.Api` is the concrete business-service reference. `ServiceTemplate` is platform scaffolding. Derive structural conventions from Customer, but never copy Customer business semantics into another bounded context.

## Evidence priority

When guidance conflicts or appears stale:

1. approved acceptance criteria/scope;
2. executable architecture/security/integration tests;
3. accepted ADRs and service documentation;
4. current production implementation/project files;
5. agent-context notes;
6. generic framework convention.

If the first four materially disagree, stop and surface the conflict instead of choosing silently.