---
name: pr-review
description: Review a completed implementation or pull-request diff against the approved Jira plan, repository architecture, security boundaries, persistence/messaging contracts, and deterministic verification requirements. Use before requesting human review or when an architecture-aware Codex review is requested.
---

# Pull Request Review

Review is evidence-driven. Do not rewrite the implementation unless explicitly handed off to `$pr-feedback-fix` or `$approved-plan-implementation`.

Always apply `$order-microservice-architecture` and `AGENTS.md`.

## Inputs

- Jira issue and acceptance criteria;
- exact approved plan and plan identifier when the change came from the automation flow;
- full diff against the intended base revision;
- deterministic build/test/CI evidence available at review time.

## Review order

1. **Scope fidelity** — detect behavior or refactoring outside the approved Jira scope.
2. **Correctness** — acceptance criteria, edge cases, nullability, cancellation, failure paths, and state transitions.
3. **Architecture** — vertical-slice independence, domain boundary, service ownership, shared-library pressure, and migration boundary.
4. **Concurrency/idempotency** — stale writes, duplicate requests, retry behavior, uniqueness races, transaction atomicity, and side effects.
5. **Security/privacy** — identity source, authorization, scopes/roles, client-safe errors, PII/secrets, and least privilege.
6. **Persistence** — query/update semantics, EF tracking, constraints/indexes, migration safety, and deployment ordering.
7. **Messaging/contracts** — outbox/inbox semantics, command/event intent, stable endpoint topology, compatibility, and transport leakage.
8. **API/error behavior** — HTTP semantics, validation, Problem Details, preconditions, ETags, and backward compatibility.
9. **Tests/evidence** — acceptance criteria coverage, architecture tests, integration tests, and any unexecuted required checks.
10. **Maintainability** — only after the above; avoid style-only noise already enforced by analyzers/CI.

## Finding quality

Report only actionable findings. Each finding must include:

- severity: `critical`, `high`, `medium`, or `low`;
- affected path and line/range when known;
- concrete failure/risk scenario;
- violated requirement, repository convention, or accepted plan step;
- smallest credible remediation direction.

Do not call speculative preferences defects. Do not request a repository pattern that contradicts the existing pure vertical-slice design.

## Merge recommendation

Return one conclusion:

- `approve`: no blocking finding and required evidence is sufficient;
- `changes_requested`: at least one correctness/architecture/security/contract issue must be fixed;
- `blocked_on_evidence`: implementation may be correct but required deterministic checks/CI evidence are missing or failing;
- `replan_required`: the implementation needs material scope/architecture changes outside the approved plan.

An agent recommendation never merges the PR. Human/branch-protection policy remains the merge authority.