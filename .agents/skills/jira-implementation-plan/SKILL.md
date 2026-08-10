---
name: jira-implementation-plan
description: Produce a read-only, architecture-aware implementation plan for one Jira issue in this repository. Use after Jira intake marks an issue ready for planning and before source edits; the plan selects the exact skills, references, and canonical examples execution should load and conforms to `.automation/schemas/plan.schema.json`.
---

# Jira Implementation Plan

Planning is read-only. Do not edit source, create migrations, change Jira, commit, push, or create a pull request.

Start with `AGENTS.md` and `$order-microservice-architecture` only long enough to classify the task. Then load the smallest focused context needed.

## Required inputs

- exact Jira issue key;
- issue context from Jira intake or permission to read it through Atlassian MCP;
- repository base revision/branch supplied by the orchestrator (normally `develop`).

## Procedure

1. Re-read the issue and acceptance criteria. Treat them as the scope boundary.
2. Identify the owning bounded context and classify architecture impact.
3. Select focused skills from:
   - `implement-vertical-slice`;
   - `change-domain-model`;
   - `change-persistence`;
   - `change-messaging`;
   - `change-security`;
   - `verify-dotnet-change`.
4. Select only the architecture references required by those impacts from `docs/agent-context/`.
5. Select 1-3 canonical production/test examples that most closely match the requested behavior. Prefer exact neighboring slices/tests over broad repository exploration.
6. Read the selected context and trace the requested change through actual affected boundaries.
7. Identify exact or best-current candidate files to create/modify. If a path is uncertain, say so rather than inventing it.
8. Map every material acceptance criterion to implementation evidence and deterministic verification evidence.
9. Evaluate bounded-context ownership, VSA independence, domain invariants/failure atomicity, concurrency/idempotency/transactions, persistence/migrations, integration contracts/messaging, security/authorization, backward compatibility, observability, and deployment only where applicable.
10. Classify risk and decide `ready`, `blocked`, or `manual_only`.
11. Produce output conforming to `.automation/schemas/plan.schema.json` (schema version `1.1`) when structured output is requested.

## Context-selection contract

The plan's `contextSelection` is part of the approval artifact, not informational prose.

It must contain:

- `skills`: only focused skills needed for execution/verification;
- `references`: exact repository reference paths to load;
- `canonicalExamples`: exact source/test paths execution should inspect first;
- `selectionReasons`: concise explanation of why each context group is needed.

Do not include every architecture skill/reference by default. The objective is enough context to execute correctly without repeated repository rediscovery.

Examples:

- validator-only Customer change -> `implement-vertical-slice`, `verify-dotnet-change`; vertical-slice/testing references; nearest validator/tests;
- Customer mutation changing invariant and schema -> add `change-domain-model`, `change-persistence`; domain/concurrency/persistence references;
- messaging infrastructure change -> `change-messaging`, `verify-dotnet-change`; messaging/testing references; no Customer HTTP context unless actually affected;
- Keycloak authorization change -> `change-security`, `verify-dotnet-change`; security/testing references; API/error context only if endpoint response/policy behavior changes.

## Risk guidance

- `low`: localized, reversible, no durable boundary change;
- `medium`: normal business behavior touching established persistence/concurrency/API semantics;
- `high`: security, migration, durable integration contract, shared-platform, cross-service behavior, or hard-to-reverse change;
- `critical`: destructive production/data/security operation or unbounded blast radius.

Choose:

- `ready`: implementable after human approval;
- `blocked`: unresolved requirement/dependency prevents a reliable implementation plan;
- `manual_only`: automation must not execute the change.

## Plan quality rules

A principal-level plan explains why each change belongs in its proposed boundary and why the selected context is sufficient.

Do not:

- invent Order aggregates/business rules absent from Jira/repository evidence;
- load broad context merely because it exists;
- introduce generic repositories/application services to make vertical slices look layered;
- hide concurrency/idempotency/migration/security/contract implications;
- classify security/durable contract/destructive persistence changes as low risk;
- claim verification that has not executed.

## Approval boundary

The complete plan, including `contextSelection`, is immutable after approval. Execution receives the exact approved plan plus orchestrator-generated fingerprint. If implementation discovers a material scope or context gap requiring new architecture boundaries, return `replan_required` instead of silently loading new scope and continuing.