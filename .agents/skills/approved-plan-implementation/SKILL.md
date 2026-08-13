---
name: approved-plan-implementation
description: Implement one explicitly approved Jira plan with bounded workspace writes. Load only the scoped owner context, focused skills, behavior references, and canonical examples recorded in the approved plan; stop for replanning when material new scope, ownership, dependency, or architecture context is required.
---

# Approved Plan Implementation

This is the workspace-write phase. Never use it to bypass the human plan gate.

## Required inputs

- Jira issue key;
- exact approved plan artifact (schema `1.2`);
- orchestrator-generated plan identifier/fingerprint;
- base revision and isolated working branch/worktree;
- explicit approval state.

If issue key, plan fingerprint, base revision, approval state, or plan schema does not match the execution request, stop as `blocked`.

## Context and placement loading

Do not load the whole repository context and do not improvise placement.

1. Read `AGENTS.md`.
2. Read approved `projectPlacement`, `fileChanges`, and `contextSelection`.
3. Load exactly the selected scoped owner document(s), focused skills, and architecture references.
4. Inspect selected canonical examples before broad repository search.
5. Inspect the owning `.csproj` before any project/package reference change.
6. Load additional ordinary source files only as required inside approved scope.

A selected owner reference may be, for example, `services/customer.md` for Customer work or `platform/shared-projects.md` for an approved shared-platform change. Do not load unrelated service/platform documents "for context".

Reading implementation dependencies inside the approved owner is normal. Discovering that correct implementation belongs in another owner/project, needs a new shared abstraction/project reference, or requires a new architecture boundary means `replan_required`.

## Execution rules

1. Reconfirm acceptance criteria, placement, file owners, context selection, and out-of-scope items.
2. Make the smallest coherent diff satisfying the approved plan in the approved owner/location.
3. Follow selected skill contracts and nearest canonical repository patterns.
4. Do not introduce cross-slice/shared abstractions unless explicitly approved and justified by demonstrated reuse.
5. Do not move service-local code into `src/Shared` for convenience.
6. Do not create a new service/bounded context, AppHost responsibility, infrastructure component, or Migrator behavior unless approved.
7. Do not add/remove project references unless the exact dependency change is approved in `projectPlacement.dependencyChanges`.
8. Do not expand scope into opportunistic refactoring, package upgrades, formatting sweeps, architecture rewrites, or unrelated cleanup.
9. Preserve source/test/project-file/CI/ADR constraints even when a generic .NET alternative is familiar.
10. On a material unknown, ownership conflict, architecture-test conflict, new dependency/security/contract/migration risk, or unapproved boundary, stop and replan.

## Focused architecture rules

When selected by the approved plan:

- `$implement-vertical-slice` -> endpoint/request/command-query/validator/handler behavior;
- `$change-domain-model` -> invariants/value objects/lifecycle/domain errors/failure atomicity;
- `$change-persistence` -> EF Core/constraints/transactions/schema/migrations/Migrator ordering;
- `$change-messaging` -> events/commands/application abstractions/outbox/inbox/topology/contracts/retry;
- `$change-security` -> Keycloak/resource-API responsibilities/validated identity/scopes/roles/least privilege;
- `$verify-dotnet-change` -> deterministic verification/evidence.

Do not apply unrelated architecture rules merely because they exist elsewhere in the repository.

## Placement invariants

Use approved owner context as the placement source of truth. Global examples:

- service business behavior stays service-owned;
- service domain invariants stay in its Domain boundary;
- service schema/migrations stay service-owned and execute through its Migrator;
- shared projects contain only documented stable cross-service responsibilities;
- AppHost is local orchestration, not production business logic;
- `infrastructure/` contains deployment/operational assets, not application/domain source;
- new bounded contexts/services require explicit approved requirements.

If an approved path is materially wrong, do not quietly relocate it. Return `replan_required` with ownership evidence.

## Verification

Use the approved verification section and `$verify-dotnet-change`. Use `docs/agent-context/testing-map.md` to identify test owners and load `architecture/testing.md` only when detailed verification semantics are needed.

Run deterministic commands narrow-to-broad. The external orchestrator/CI should independently capture exit codes. A check is successful only if it executed and returned success.

When project references, solution membership, shared libraries, AppHost, infrastructure, or Migrators change, verify the affected graph/build in addition to feature tests. GitHub CI remains final PR authority for checks unavailable locally.

## Completion review

Before reporting success:

1. inspect the full diff against the approved plan;
2. verify every acceptance criterion has implementation/test evidence;
3. verify every changed/created file belongs to its approved owner/project/folder;
4. verify project-reference changes exactly match approved `projectPlacement.dependencyChanges`;
5. verify no unapproved shared dependency/architecture boundary/context was introduced;
6. check for accidental generated files, secrets, formatting/package churn, or unrelated edits;
7. report residual risks and `not_run`/`blocked` verification honestly;
8. do not merge, deploy, or transition Jira to Done.

When structured output is requested, conform to `.automation/schemas/execution-result.schema.json` schema `1.2`; populate placement/context compliance from the actual diff and loaded context.

Terminal statuses: `success`, `failed`, `blocked`, or `replan_required`.