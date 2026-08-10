---
name: pr-review
description: Review a completed implementation or PR diff against the approved Jira plan, its selected architecture context, repository constraints, and deterministic evidence. Use before human review or for architecture-aware Codex review; do not edit unless handed off to a fix/implementation skill.
---

# Pull Request Review

Review is evidence-driven. Do not rewrite implementation unless explicitly handed off to `$pr-feedback-fix` or `$approved-plan-implementation`.

## Inputs

- Jira issue/acceptance criteria;
- exact approved plan/fingerprint when applicable;
- approved `contextSelection`;
- full diff against intended base revision;
- deterministic build/test/CI evidence available at review time.

## Context loading

Read `AGENTS.md`, then load the focused skills/references recorded in the approved plan. Inspect its canonical examples when comparing architecture/pattern fidelity.

If the diff touches an architecture boundary not present in the approved plan/context selection, treat that as scope drift and normally return `replan_required` rather than silently broadening review assumptions.

## Review order

1. **Scope fidelity** — no behavior/refactoring or new architecture boundary outside approved scope.
2. **Correctness** — acceptance criteria, edge cases, nullability, cancellation, failure paths, state transitions.
3. **Selected architecture contracts** — evaluate only relevant VSA/domain/persistence/messaging/security contracts deeply, plus global invariants.
4. **Concurrency/idempotency/transactions** — when the affected behavior can race/retry or mutate state.
5. **Security/privacy** — identity source, authorization, least privilege, safe errors/PII/secrets where affected.
6. **Compatibility** — API/schema/integration/topology behavior where affected.
7. **Tests/evidence** — acceptance traceability, architecture tests, integration tests, unexecuted required checks.
8. **Maintainability** — after correctness/contracts; avoid style noise already enforced by analyzers/CI.

## Finding quality

Report only actionable findings. Each finding must include:

- severity: `critical`, `high`, `medium`, or `low`;
- affected path/line when known;
- concrete failure/risk scenario;
- violated acceptance criterion, approved plan item, repository rule, selected reference, or deterministic check;
- smallest credible remediation direction.

Do not call generic preferences defects. Do not request a pattern that conflicts with this repository's pure vertical-slice approach.

## Merge recommendation

Return one:

- `approve` — no blocking finding and required evidence is sufficient;
- `changes_requested` — in-scope correctness/architecture/security/contract defect must be fixed;
- `blocked_on_evidence` — required deterministic checks/CI are missing or failing;
- `replan_required` — correction needs material scope/context/architecture change outside the approved plan.

An agent recommendation never merges the PR.