---
name: verify-dotnet-change
description: Select and run deterministic repository verification for an implemented change without modifying scope. Use after implementation or when validating a PR/diff. Reports only checks that actually executed and treats GitHub CI as final authority.
---

# Verify .NET Change

Load `docs/agent-context/architecture/testing.md` and inspect `.github/workflows/dotnet-ci.yml` before choosing broad verification.

## Procedure

1. Inspect the diff and list affected projects/boundaries.
2. Map each affected boundary to the repository's actual test projects/checks.
3. Run narrow tests first for fast feedback.
4. Run affected Release builds with analyzers/warnings-as-errors.
5. Run broader affected architecture/integration/infrastructure checks.
6. Record every check as `passed`, `failed`, `not_run`, or `blocked` with the actual command and reason.
7. Do not edit production code merely to silence a verification result unless the caller explicitly switches back to an implementation/fix skill.

## Rules

- Never report a predicted check as passed.
- Never weaken an architecture/security/reliability test because a diff violates it.
- Never suppress analyzers without explicit justification in scope.
- If local infrastructure prevents a check, mark it `blocked`/`not_run` and rely on CI rather than fabricating evidence.
- GitHub PR CI remains final deterministic authority.