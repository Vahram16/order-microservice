---
name: pr-feedback-fix
description: Apply accepted, actionable pull-request review feedback to an existing automation branch without expanding the approved Jira scope. Use after review comments or CI findings require a bounded follow-up implementation and verification pass.
---

# Pull Request Feedback Fix

Use this skill only on an existing implementation branch/PR. Preserve the original Jira issue and approved-plan boundary.

Always apply `$order-microservice-architecture` and `AGENTS.md`.

## Procedure

1. Read the current PR diff, unresolved review feedback, and latest CI/check results.
2. Classify each item:
   - `actionable_in_scope`;
   - `already_resolved`;
   - `needs_clarification`;
   - `replan_required` because it materially expands scope/architecture;
   - `not_applicable` with a concrete repository-based reason.
3. Implement only `actionable_in_scope` items.
4. Keep changes minimal and consistent with the existing approved plan.
5. Add/update tests when feedback exposes missing behavior or regression coverage.
6. Re-run the narrow affected checks, then all verification required by the changed boundary.
7. Re-read the full diff to ensure the fix did not introduce unrelated changes.

## Safety rules

- Do not silently accept feedback that conflicts with repository architecture, security requirements, an accepted ADR, or the approved Jira scope.
- Do not resolve a review thread merely because code changed; resolve it only when the requested issue is actually addressed and verification supports the fix.
- A reviewer request for a breaking contract, security-boundary change, destructive migration, new cross-service abstraction, or material requirement change triggers `replan_required`.
- CI failures are evidence to investigate, not instructions to weaken tests, analyzers, security, or architecture checks.
- Never merge or deploy.

## Output

Return:

- feedback item -> disposition mapping;
- files changed for accepted fixes;
- tests/checks executed and outcomes;
- remaining unresolved feedback;
- whether the PR is ready for `$pr-review` again or requires replanning/human clarification.