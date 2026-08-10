# Agent Context Map

This directory is a progressive-disclosure knowledge base for coding agents. `AGENTS.md` stays intentionally small; task-specific skills load only the references required by the current change.

Repository source, executable architecture tests, accepted ADRs, and CI remain authoritative. These notes are navigation and decision aids, not a replacement for reading the affected implementation.

## Load by change type

| Change | Read first | Canonical evidence |
| --- | --- | --- |
| New/modified business use case | `architecture/vertical-slice.md` | `CustomerVerticalSliceArchitectureTests.cs` and nearest Customer slice |
| Aggregate/value-object/invariant | `architecture/domain-boundary.md` | `CustomerDomainBoundaryTests.cs`, `CustomerDomainTests.cs` |
| HTTP contract/error behavior | `architecture/api-and-errors.md` | Customer endpoints, `CustomerHttpResults.cs`, `docs/error-handling.md` |
| ETag/idempotency/transaction behavior | `architecture/concurrency-idempotency.md` | AddingAddress/UpdatingAddress handlers and Customer integration tests |
| EF Core/schema/migration | `architecture/persistence.md` | `CustomerDbContext.cs`, Migrator, persistence tests |
| Events/commands/outbox/retry | `architecture/messaging.md` | messaging ADRs, shared messaging abstractions, reliability tests |
| Authentication/authorization/claims | `architecture/security.md` | `docs/keycloak-integration.md`, `Microservices.Security`, security tests |
| Test selection / completion | `architecture/testing.md` | `.github/workflows/dotnet-ci.yml` and affected test projects |

## Core rule

Load the smallest set of references that fully covers the affected architectural boundaries. Do not preload every document for every task.

For example:

- a Customer validation-only change normally needs `vertical-slice.md` and `testing.md` plus the nearest validator/tests;
- a new Customer mutation normally needs `vertical-slice.md`, `api-and-errors.md`, `concurrency-idempotency.md`, `domain-boundary.md`, and `testing.md`;
- a MassTransit/outbox change needs `messaging.md` and `testing.md`, not the Customer HTTP documents;
- a Keycloak/authorization change needs `security.md`, `api-and-errors.md` if HTTP behavior changes, and `testing.md`.

## Reference service

`Customer.Api` is the repository's concrete business-service reference. `ServiceTemplate` is infrastructure scaffolding. Derive structural conventions from Customer, but never copy Customer business semantics into another bounded context.

## Evidence priority

When guidance conflicts or appears stale, resolve in this order:

1. acceptance criteria and explicit human-approved scope;
2. executable architecture/security/integration tests;
3. accepted ADRs and service documentation;
4. current production implementation;
5. these agent-context notes;
6. generic framework or architectural convention.

If the first four disagree materially, stop and surface the conflict rather than choosing silently.