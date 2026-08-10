---
name: approved-plan-implementation
description: Implement one explicitly approved Jira implementation plan in this repository with bounded workspace writes, repository-specific architecture rules, deterministic verification, and structured execution reporting. Use only after human approval; never use for unapproved or materially changed plans.
---

# Approved Plan Implementation

This skill is the workspace-write phase. It must never be used to bypass the human plan gate.

Always apply `$order-microservice-architecture` and `AGENTS.md`.

## Required inputs

- Jira issue key;
- the exact approved plan artifact;
- orchestrator-generated plan identifier/fingerprint;
- base revision and working branch/worktree;
- explicit approval state.

If the approval artifact, plan identifier, issue key, or base revision does not match the execution request, stop as `blocked`.

## Execution rules

1. Reconfirm the approved scope before editing.
2. Inspect the exact neighboring implementation/tests named by the plan.
3. Make the smallest coherent diff that satisfies the approved acceptance criteria.
4. Preserve the existing pure vertical-slice structure and domain/infrastructure boundaries.
5. Do not introduce a new cross-slice/shared abstraction unless the approved plan explicitly justifies it with demonstrated reuse.
6. Do not expand scope into opportunistic refactoring, package upgrades, formatting sweeps, architecture rewrites, or unrelated cleanup.
7. When implementation reveals a material unknown, security concern, breaking contract, new migration risk, or architecture change absent from the approved plan, stop and return `replan_required`.

## Repository-specific implementation requirements

- Use shared `ICommand`/`IQuery` contracts and matching handlers for business slices.
- Keep one top-level responsibility per source file and avoid sibling-slice dependencies.
- Keep domain code framework-free and let the domain enforce business invariants.
- Preserve layered error semantics; do not leak exception/database/internal details into client-visible errors.
- Preserve ETag/concurrency/idempotency semantics when touching mutable Customer-style behavior.
- Use explicit service-owned transaction boundaries when multiple persistence effects must be atomic.
- Keep MassTransit/RabbitMQ behind the approved integration messaging abstractions.
- Keep migrations out of API startup and use the owning Migrator deployment boundary.
- Preserve Keycloak/resource API responsibility and least privilege.

## Verification

Run deterministic commands in the workspace. Start narrow for feedback speed, then execute all affected test projects/checks required by the approved plan and `AGENTS.md`.

At minimum for changed .NET projects:

```bash
dotnet restore <project>
dotnet build <project> --configuration Release --no-restore
dotnet test <test-project> --configuration Release --no-restore
```

For messaging, Keycloak, shared-platform, persistence, or architecture changes, run the dedicated repository checks identified by `$order-microservice-architecture` and leave full CI as the final authority.

Do not claim a command passed unless the command actually executed successfully. The external orchestrator/CI should independently capture process exit codes; the agent's report is supplementary evidence, not the source of truth.

## Completion review

Before reporting success:

1. inspect the full diff against the approved plan;
2. verify every acceptance criterion has implementation and test evidence;
3. check for accidental generated files, secrets, broad formatting, package churn, or unrelated edits;
4. identify residual risks and any verification that could not run locally;
5. do not merge, deploy, or transition Jira to Done.

When structured output is requested, conform to `.automation/schemas/execution-result.schema.json`.

Allowed terminal statuses are:

- `success`: implementation is complete and locally verified as far as the plan requires;
- `failed`: deterministic verification failed and the result contains the failing checks;
- `blocked`: an external dependency/environment problem prevents completion;
- `replan_required`: implementation would materially exceed or contradict the approved plan.