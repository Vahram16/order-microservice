---
name: verify-dotnet-change
description: Select and run deterministic repository verification for an implemented change without modifying scope. Use after implementation or when validating a PR/diff. Reports only checks that actually executed and treats GitHub CI as final authority.
---

# Verify .NET Change

Load `docs/agent-context/testing-map.md` first to identify test ownership. Load `docs/agent-context/architecture/testing.md` and inspect `.github/workflows/dotnet-ci.yml` only when choosing detailed/broad verification.

## Procedure

1. Inspect the diff and list affected owners/projects/architecture boundaries.
2. Map each affected boundary to actual test projects/checks using `testing-map.md`.
3. Run narrow owning tests first for fast feedback.
4. Run affected Release builds with analyzers/warnings-as-errors.
5. Run broader affected architecture/integration/infrastructure checks only where the diff requires them.
6. Record every check as `passed`, `failed`, `not_run`, or `blocked` with actual command/outcome/reason.
7. Do not edit production code merely to silence verification unless the caller explicitly switches back to an implementation/fix skill.

## Rules

- Never report a predicted check as passed.
- Never weaken architecture/security/reliability tests because a diff violates them.
- Never suppress analyzers without explicit in-scope justification.
- If local infrastructure prevents a check, mark it `blocked`/`not_run` and rely on CI rather than fabricating evidence.
- When solution membership/project references/agent-context routing change, run the corresponding deterministic structure check.
- GitHub PR CI remains final deterministic authority.