---
name: pr-review
description: Review a completed implementation or PR diff against the approved Jira plan, approved project placement, selected scoped owner/architecture context, repository constraints, and deterministic evidence. Use before human review or for architecture-aware Codex review; do not edit unless handed off to a fix/implementation skill.
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

1. Read `AGENTS.md`.
2. Use approved `contextSelection` rather than loading repository-wide context.
3. Load the approved scoped owner document(s), focused skills, and behavior references.
4. Inspect canonical examples recorded in the plan.
5. When placement/dependencies changed, inspect the owning `.csproj`, `Microservices.Boilerplate.slnx`, and actual diff.

Compare every changed file's owner/project/folder with approved `fileChanges.owner` and `projectPlacement`. Compare every project-reference addition/removal with approved `projectPlacement.dependencyChanges`.

If the diff touches an unapproved owner, shared project, new project, dependency, or architecture boundary, normally return `replan_required` rather than broadening review assumptions.

## Review order

1. **Scope and placement fidelity** — no unapproved behavior/refactoring, file owner, project dependency, new project/shared abstraction, or architecture boundary.
2. **Correctness** — acceptance criteria, edge cases, nullability, cancellation, failure paths, state transitions.
3. **Selected architecture contracts** — evaluate only relevant VSA/domain/persistence/messaging/security rules plus global invariants.
4. **Dependency direction** — service/shared/AppHost/Migrator boundaries and `.csproj` references remain valid; service-local behavior has not leaked into shared projects.
5. **Concurrency/idempotency/transactions** — where the affected behavior can race/retry/mutate state.
6. **Security/privacy** — identity source, authorization, least privilege, safe errors/PII/secrets where affected.
7. **Compatibility** — API/schema/integration/topology behavior where affected.
8. **Tests/evidence** — acceptance traceability, test owner from `testing-map.md`, architecture/integration tests, and missing required checks.
9. **Maintainability** — after correctness/contracts; avoid style noise already enforced by analyzers/CI.

## Finding quality

Report only actionable findings. Each finding includes severity, affected path/line when known, concrete failure/risk scenario, violated requirement/placement/rule/evidence, and the smallest credible remediation direction.

Do not label generic preferences as defects or request patterns conflicting with this repository's pure vertical-slice/ownership model.

## Merge recommendation

Return one:

- `approve` — no blocking finding, placement compliant, required evidence sufficient;
- `changes_requested` — in-scope defect can be fixed without changing approved placement/scope;
- `blocked_on_evidence` — required deterministic checks/CI missing or failing;
- `replan_required` — correction requires material scope/context/owner/project/dependency/architecture change.

An agent recommendation never merges the PR.