---
name: approved-plan-implementation
description: Implement one explicitly approved Jira plan with bounded workspace writes. Loads only the focused skills, references, canonical examples, and project-placement context recorded in the approved plan; stops for replanning when material new scope, ownership, dependency, or architecture context is required.
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

Do not automatically load the entire architecture corpus and do not improvise code placement.

1. Read `AGENTS.md`.
2. Read the approved plan's `projectPlacement`, proposed `fileChanges`, and `contextSelection`.
3. Load exactly the selected focused skills and architecture reference paths.
4. If `docs/agent-context/project-structure.md` is selected, use it to verify target project/folder ownership and dependency direction before creating/moving files.
5. Inspect the selected canonical examples before broad repository search.
6. Inspect the owning `.csproj` before adding/removing project/package references.
7. Load additional ordinary source files only as required to implement the approved file/behavior scope.

Reading an implementation dependency inside the approved owner is normal. Discovering that correct implementation belongs in another bounded context/project, needs a new shared abstraction/project reference, or requires a materially new architecture boundary is not normal discovery: return `replan_required`.

## Execution rules

1. Reconfirm approved acceptance criteria, `projectPlacement`, target file owners, context selection, and out-of-scope items before editing.
2. Make the smallest coherent diff that satisfies the approved plan **in the approved owner/location**.
3. Follow the selected skill contracts and canonical repository patterns.
4. Do not introduce cross-slice/shared abstractions unless explicitly approved and justified by demonstrated reuse.
5. Do not move service-local code into `src/Shared` for convenience.
6. Do not create a new service/bounded context, AppHost responsibility, infrastructure component, or Migrator behavior unless explicitly present in the approved plan.
7. Do not add/remove project references unless the exact dependency change is approved in `projectPlacement.dependencyChanges`.
8. Do not expand scope into opportunistic refactoring, package upgrades, formatting sweeps, architecture rewrites, or unrelated cleanup.
9. Preserve source/test/project-file/CI/ADR constraints even when an alternative generic .NET pattern is familiar.
10. On a material unknown, incompatible requirement, architecture-test conflict, different code owner, new project dependency, new security/contract/migration risk, or unapproved cross-boundary dependency, stop and replan.

## Boundary rules

When selected by the plan:

- `$implement-vertical-slice` governs endpoint/request/command-query/validator/handler placement;
- `$change-domain-model` governs invariants, value objects, lifecycle, domain errors, and failure atomicity;
- `$change-persistence` governs EF Core, named constraints, transactions, schema, migrations, and Migrator ordering;
- `$change-messaging` governs event/command intent, approved abstractions, outbox/inbox, topology, contracts, and retry policy;
- `$change-security` governs Keycloak/resource-API responsibilities, validated identity, scopes/roles, and least privilege;
- `$verify-dotnet-change` governs deterministic verification and evidence reporting.

`docs/agent-context/project-structure.md` governs repository ownership/placement when selected. Do not apply rules from unrelated boundaries merely because they exist elsewhere in the repository.

## Placement invariants

- Customer business use cases remain in Customer vertical slices.
- Customer domain invariants remain in `Customer.Api/Domain`.
- Customer EF/schema/migrations remain service-owned under `Customer.Api/Persistence`; migration execution remains in `Customer.Migrator`.
- Shared projects contain only their documented stable cross-service responsibilities.
- AppHost remains local development orchestration, not production business logic.
- Infrastructure folders remain deployment/operational assets, not application/domain source.
- A new bounded context/service requires explicit approved requirements.

If an approved file path turns out to be structurally wrong in a material way, do not quietly choose a different architecture. Return `replan_required` with the discovered ownership evidence.

## Verification

Use the approved plan's verification section and `$verify-dotnet-change`.

Run deterministic commands in the workspace, narrow-to-broad. The external orchestrator/CI should independently capture process exit codes. A check is successful only if it actually executed and returned success.

When project references, solution membership, shared libraries, AppHost, or Migrators change, verify the affected project graph/build in addition to feature tests.

GitHub CI remains final pull-request authority, especially for real PostgreSQL/RabbitMQ, Keycloak, architecture, and cross-project checks unavailable locally.

## Completion review

Before reporting success:

1. inspect the full diff against the approved plan;
2. verify every acceptance criterion has implementation and test evidence;
3. verify every changed/created file belongs to its approved `owner` and project/folder;
4. verify observed project-reference changes exactly match approved `projectPlacement.dependencyChanges`;
5. verify no unapproved shared dependency/architecture boundary was introduced;
6. confirm no unapproved architecture context was introduced;
7. check for accidental generated files, secrets, broad formatting, package churn, or unrelated edits;
8. list residual risks and any `not_run`/`blocked` verification honestly;
9. do not merge, deploy, or transition Jira to Done.

When structured output is requested, conform to `.automation/schemas/execution-result.schema.json` (schema `1.2`). Populate `placementCompliance` from the actual diff/project graph; do not mark it compliant when an unapproved owner or dependency change exists.

Terminal statuses:

- `success` — complete in the approved owner/location with required local deterministic verification executed as far as the environment permits and placement compliant;
- `failed` — required deterministic verification executed and failed;
- `blocked` — external dependency/environment/approval mismatch prevents completion;
- `replan_required` — correct implementation would materially exceed the approved plan/context/ownership/dependency placement.