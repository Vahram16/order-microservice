---
name: jira-implementation-plan
description: Produce a read-only, architecture-aware implementation plan for one Jira issue in this repository. Use after Jira intake marks an issue ready for planning and before any source edits; the result is intended for human approval and machine validation against `.automation/schemas/plan.schema.json`.
---

# Jira Implementation Plan

Planning is read-only. Do not edit source, create migrations, change Jira, commit, push, or create a pull request.

Always apply `$order-microservice-architecture` while planning.

## Inputs

- exact Jira issue key;
- issue context from Jira intake or permission to read it through Atlassian MCP;
- repository base revision/branch supplied by the orchestrator (normally `develop` for this repository).

## Procedure

1. Re-read the issue and acceptance criteria. Treat them as the scope boundary.
2. Inspect `AGENTS.md` and route to only the architecture documentation relevant to the issue.
3. Locate the nearest analogous production implementation and its tests. Prefer executable repository conventions over generic framework guidance.
4. Trace the change through the actual boundaries it touches: endpoint/request -> command/query -> validation -> handler -> domain -> persistence -> integration/security/platform as applicable.
5. Identify exact or best-current candidate files to create/modify. If a path is uncertain, state that uncertainty rather than inventing a file.
6. Define acceptance-to-verification traceability: every material acceptance criterion must map to one or more deterministic tests/checks.
7. Evaluate:
   - bounded-context ownership;
   - vertical-slice independence;
   - domain invariants/failure atomicity;
   - concurrency/idempotency/transaction impact;
   - persistence/schema/migration impact;
   - integration-contract/messaging impact;
   - authentication/authorization/security impact;
   - backward compatibility;
   - observability/deployment impact.
8. Classify risk:
   - `low`: localized, reversible, no durable boundary change;
   - `medium`: normal business behavior touching persistence/concurrency/API semantics within established patterns;
   - `high`: security, migration, durable integration contract, shared-platform, cross-service behavior, or hard-to-reverse change;
   - `critical`: destructive production/data/security operation or change whose blast radius cannot be safely bounded by this workflow.
9. Choose decision:
   - `ready`: plan is implementable after human approval;
   - `blocked`: implementation cannot proceed until listed blocking questions/dependencies are resolved;
   - `manual_only`: automation must not execute the change.
10. Produce output conforming to `.automation/schemas/plan.schema.json` when structured output is requested.

## Plan quality rules

A professional plan explains *why* each change belongs in its proposed boundary. It must not prescribe speculative abstractions just because they are common in other .NET architectures.

Do not:

- invent Order aggregates/business rules absent from Jira/repository evidence;
- introduce generic repositories or application services to make a vertical slice look layered;
- hide concurrency/idempotency implications;
- assume a database migration is harmless;
- convert a security or contract change into a low-risk task;
- claim verification that has not been executed.

## Approval boundary

The plan is an immutable approval artifact. Once approved, execution receives the exact approved plan plus an orchestrator-generated plan identifier/fingerprint. If implementation discovers a material scope change, stop and return `blocked`/`replan_required`; do not silently expand the approved plan.