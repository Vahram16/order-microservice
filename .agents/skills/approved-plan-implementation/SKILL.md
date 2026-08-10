---
name: approved-plan-implementation
description: Implement one explicitly approved Jira plan with bounded workspace writes. Loads only the focused skills, architecture references, and canonical examples recorded in the approved plan's `contextSelection`; stops for replanning when material new scope or architecture context is required.
---

# Approved Plan Implementation

This is the workspace-write phase. Never use it to bypass the human plan gate.

## Required inputs

- Jira issue key;
- exact approved plan artifact (schema `1.1`);
- orchestrator-generated plan identifier/fingerprint;
- base revision and isolated working branch/worktree;
- explicit approval state.

If issue key, plan fingerprint, base revision, approval state, or plan schema does not match the execution request, stop as `blocked`.

## Context loading

Do not automatically load the entire architecture corpus.

1. Read `AGENTS.md`.
2. Read the approved plan's `contextSelection`.
3. Load exactly the selected focused skills and architecture reference paths.
4. Inspect the selected canonical examples before broad repository search.
5. Load additional source files only as required to implement the approved file/behavior scope.

If implementation requires a materially new architecture boundary not represented in the approved `contextSelection` (for example a migration, integration contract, or security change), return `replan_required`. Do not silently expand the context and approval scope.

## Execution rules

1. Reconfirm approved acceptance criteria, file changes, context selection, and out-of-scope items before editing.
2. Make the smallest coherent diff that satisfies the approved plan.
3. Follow the selected skill contracts and canonical repository patterns.
4. Do not introduce cross-slice/shared abstractions unless explicitly approved and justified by demonstrated reuse.
5. Do not expand scope into opportunistic refactoring, package upgrades, formatting sweeps, architecture rewrites, or unrelated cleanup.
6. Preserve source/test/CI/ADR constraints even when an alternative generic .NET pattern is familiar.
7. On a material unknown, incompatible requirement, architecture-test conflict, new security/contract/migration risk, or unapproved cross-boundary dependency, stop and replan.

## Boundary rules

When selected by the plan:

- `$implement-vertical-slice` governs endpoint/request/command-query/validator/handler placement;
- `$change-domain-model` governs invariants, value objects, lifecycle, domain errors, and failure atomicity;
- `$change-persistence` governs EF Core, named constraints, transactions, schema, migrations, and Migrator ordering;
- `$change-messaging` governs event/command intent, approved abstractions, outbox/inbox, topology, contracts, and retry policy;
- `$change-security` governs Keycloak/resource-API responsibilities, validated identity, scopes/roles, and least privilege;
- `$verify-dotnet-change` governs deterministic verification and evidence reporting.

Do not apply rules from an unrelated boundary simply because they exist elsewhere in the repository.

## Verification

Use the approved plan's verification section and `$verify-dotnet-change`.

Run deterministic commands in the workspace, narrow-to-broad. The external orchestrator/CI should independently capture process exit codes. A check is successful only if it actually executed and returned success.

GitHub CI remains final pull-request authority, especially for real PostgreSQL/RabbitMQ, Keycloak, architecture, and cross-project checks unavailable locally.

## Completion review

Before reporting success:

1. inspect the full diff against the approved plan;
2. verify every acceptance criterion has implementation and test evidence;
3. confirm no unapproved architecture context/boundary was introduced;
4. check for accidental generated files, secrets, broad formatting, package churn, or unrelated edits;
5. list residual risks and any `not_run`/`blocked` verification honestly;
6. do not merge, deploy, or transition Jira to Done.

When structured output is requested, conform to `.automation/schemas/execution-result.schema.json`.

Terminal statuses:

- `success` — complete with required local deterministic verification executed as far as the environment permits;
- `failed` — required deterministic verification executed and failed;
- `blocked` — external dependency/environment/approval mismatch prevents completion;
- `replan_required` — correct implementation would materially exceed the approved plan/context.