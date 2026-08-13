---
name: jira-implementation-plan
description: Produce a read-only, architecture-aware implementation plan for one Jira issue in this repository. Use after Jira intake marks an issue ready for planning and before source edits; the plan identifies the owning project/folder, selects exact scoped owner context, focused skills/references/canonical examples, and conforms to `.automation/schemas/plan.schema.json`.
---

# Jira Implementation Plan

Planning is read-only. Do not edit source, create migrations, change Jira, commit, push, or create a pull request.

## Required inputs

- exact Jira issue key;
- issue context from Jira intake or permission to read it through Atlassian MCP;
- repository base revision/branch supplied by the orchestrator (normally `develop`).

## Procedure

1. Re-read the issue and acceptance criteria. Treat them as the scope boundary.
2. Read `AGENTS.md` and `docs/agent-context/project-map.md`.
3. Load only the matching owner context needed to establish placement:
   - Customer -> `docs/agent-context/services/customer.md`;
   - ServiceTemplate/new-service scaffolding -> `docs/agent-context/services/service-template.md`;
   - shared libraries -> `docs/agent-context/platform/shared-projects.md`;
   - AppHost -> `docs/agent-context/platform/apphost.md`;
   - deployment assets -> `docs/agent-context/platform/infrastructure.md`.
4. Inspect `Microservices.Boilerplate.slnx`, the owning `.csproj`, and nearest production/test evidence as needed to confirm:
   - owner kind / bounded context;
   - target project(s) and folder(s);
   - direct dependency direction;
   - test owner(s);
   - whether any shared/AppHost/infrastructure/Migrator/new-project change is justified.
5. Populate `projectPlacement` with owner, target/test projects, target folders, any new projects/dependency changes, placement reasons, and shared-abstraction justification when applicable.
6. Classify architecture impact and select only focused skills actually needed:
   - `implement-vertical-slice`;
   - `change-domain-model`;
   - `change-persistence`;
   - `change-messaging`;
   - `change-security`;
   - `verify-dotnet-change`.
7. Select only behavior references required by those impacts from `docs/agent-context/architecture/`.
8. Put the scoped owner document in `contextSelection.references` when placement/ownership is needed during execution. Do **not** include unrelated service/platform documents.
9. Select 1-3 nearest canonical production/test examples. Prefer neighboring slices/tests over broad repository exploration.
10. Identify exact or best-current candidate files. Every `fileChanges` entry must include an owner matching approved placement. Do not invent uncertain paths.
11. Map every material acceptance criterion to implementation and deterministic verification evidence.
12. Evaluate only applicable concerns: bounded-context ownership, VSA, domain invariants/failure atomicity, concurrency/idempotency/transactions, persistence/migrations, integration contracts/messaging, security/authorization, compatibility, observability, deployment, and project dependency impact.
13. Classify risk and choose `ready`, `blocked`, or `manual_only`.
14. Produce `.automation/schemas/plan.schema.json` schema `1.2` when structured output is requested.

## Context-selection contract

`projectPlacement` and `contextSelection` are approval artifacts, not advisory prose.

`contextSelection` must contain:

- `skills`: only focused execution/verification skills;
- `references`: the minimum scoped owner + architecture references required;
- `canonicalExamples`: exact source/test paths execution should inspect first;
- `selectionReasons`: why each selected context item is necessary.

Examples:

- Customer validator-only change -> `services/customer.md`, `architecture/vertical-slice.md`, nearest validator/tests, `implement-vertical-slice`, `verify-dotnet-change`;
- Customer mutation changing an invariant -> additionally `architecture/domain-boundary.md` / `change-domain-model`; concurrency/persistence only if actually affected;
- shared messaging infrastructure -> `platform/shared-projects.md`, `architecture/messaging.md`, messaging tests, `change-messaging`; no Customer context;
- production Keycloak asset change -> `platform/infrastructure.md`, `architecture/security.md`; AppHost context only if local orchestration also changes;
- new real bounded context -> explicit approved business ownership + `services/service-template.md` for scaffolding + only needed shared/platform references. Never infer an Order service from repository naming.

Do not include every skill/reference "for safety". If the planner cannot identify a reliable owner or business rule, block/replan rather than compensating with broad context.

## Risk guidance

- `low`: localized, reversible, no durable boundary/dependency change;
- `medium`: normal business behavior touching established persistence/concurrency/API semantics;
- `high`: security, migration, durable integration contract, new shared dependency/project, shared platform, cross-service behavior, or hard-to-reverse change;
- `critical`: destructive production/data/security operation or unbounded blast radius.

A proposal for a new project, project reference, shared abstraction, security boundary, durable contract, or destructive persistence behavior cannot be hidden inside a normal low-risk slice plan.

## Plan quality

A principal-level plan explains:

- why the selected owner/project/folder is correct;
- why dependency direction remains valid;
- which exact repository examples support the design;
- why the selected context is sufficient and unrelated context is unnecessary;
- what would trigger replanning.

Do not invent business/domain behavior, speculative services/shared abstractions, generic repository/application-service layers, or verification evidence.

## Approval boundary

The complete plan, including `projectPlacement`, file owners, and `contextSelection`, is immutable after approval. If implementation discovers that correct work belongs in a materially different owner, needs a new project reference/shared abstraction, or requires a new architecture boundary, return `replan_required` instead of silently relocating code or expanding context.