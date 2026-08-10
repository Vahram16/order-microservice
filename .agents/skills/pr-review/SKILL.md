---
name: pr-review
description: Review a completed implementation or PR diff against the approved Jira plan, approved project placement, selected architecture context, repository constraints, and deterministic evidence. Use before human review or for architecture-aware Codex review; do not edit unless handed off to a fix/implementation skill.
---

# Pull Request Review

Review is evidence-driven. Do not rewrite implementation unless explicitly handed off to `$pr-feedback-fix` or `$approved-plan-implementation`.

## Inputs

- Jira issue/acceptance criteria;
- exact approved plan/fingerprint when applicable;
- approved `projectPlacement` and `contextSelection`;
- full diff against intended base revision;
- deterministic build/test/CI evidence available at review time.

## Context and placement loading

Read `AGENTS.md`. Read `docs/agent-context/project-structure.md` when ownership, new files, project references, shared code, AppHost, infrastructure, or Migrators are touched. Then load the focused skills/references recorded in the approved plan and inspect canonical examples.

Compare every changed file's actual owner/project/folder with approved `fileChanges.owner` and `projectPlacement`. Compare every project-reference addition/removal with approved `projectPlacement.dependencyChanges`.

If the diff touches an unapproved owner, shared project, new project, dependency, or architecture boundary, treat that as scope/placement drift and normally return `replan_required` rather than silently broadening review assumptions.

## Review order

1. **Scope and placement fidelity** — no behavior/refactoring, file owner, project dependency, new project/shared abstraction, or architecture boundary outside approved scope.
2. **Correctness** — acceptance criteria, edge cases, nullability, cancellation, failure paths, state transitions.
3. **Selected architecture contracts** — evaluate only relevant VSA/domain/persistence/messaging/security contracts deeply, plus global invariants.
4. **Dependency direction** — service/shared/AppHost/Migrator boundaries and `.csproj` references remain valid; service-local behavior has not leaked into shared projects.
5. **Concurrency/idempotency/transactions** — when affected behavior can race/retry or mutate state.
6. **Security/privacy** — identity source, authorization, least privilege, safe errors/PII/secrets where affected.
7. **Compatibility** — API/schema/integration/topology behavior where affected.
8. **Tests/evidence** — acceptance traceability, owning test project, architecture tests, integration tests, unexecuted required checks.
9. **Maintainability** — after correctness/contracts; avoid style noise already enforced by analyzers/CI.

## Finding quality

Report only actionable findings. Each finding must include:

- severity: `critical`, `high`, `medium`, or `low`;
- affected path/line when known;
- concrete failure/risk scenario;
- violated acceptance criterion, approved placement/plan item, repository rule, selected reference, or deterministic check;
- smallest credible remediation direction.

Do not call generic preferences defects. Do not request a pattern that conflicts with this repository's pure vertical-slice or ownership model.

## Merge recommendation

Return one:

- `approve` — no blocking finding, placement is compliant, and required evidence is sufficient;
- `changes_requested` — an in-scope correctness/architecture/security/contract defect must be fixed without changing approved placement/scope;
- `blocked_on_evidence` — required deterministic checks/CI are missing or failing;
- `replan_required` — correction needs material scope/context/owner/project/dependency/architecture change outside the approved plan.

An agent recommendation never merges the PR.