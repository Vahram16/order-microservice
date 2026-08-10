# Context Selection Contract

The automation treats context as an approved execution dependency, not an unbounded prompt.

## Goal

Provide enough repository-specific knowledge for correct implementation while avoiding repeated broad repository discovery and avoiding loading unrelated architecture manuals into every Codex run.

This is an optimization for both quality and token/cycle-time efficiency. It does not assume that more documentation automatically means fewer tokens.

## Planning owns context selection

The read-only planning stage classifies the change and emits `contextSelection` in `plan.json`:

```json
{
  "skills": [
    "implement-vertical-slice",
    "change-domain-model",
    "verify-dotnet-change"
  ],
  "references": [
    "docs/agent-context/architecture/vertical-slice.md",
    "docs/agent-context/architecture/domain-boundary.md",
    "docs/agent-context/architecture/testing.md"
  ],
  "canonicalExamples": [
    "src/Services/Customer/Customer.Api/Features/Customers/UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs",
    "tests/Customer.Api.Tests/CustomerDomainTests.cs"
  ],
  "selectionReasons": [
    "The task changes one Customer mutation and a domain invariant; it does not change persistence schema, messaging, or identity configuration."
  ]
}
```

The example is illustrative only; the planner must derive actual paths from the task/repository.

## Selection rules

1. Start from `AGENTS.md` and the architecture router.
2. Select focused skills by actual affected boundary.
3. Select architecture references needed to understand those boundaries.
4. Prefer 1-3 nearest canonical source/test examples; `preferredCanonicalExampleLimit` is configurable policy, not a hard correctness limit.
5. Do not select unrelated areas "just in case".
6. Include `verify-dotnet-change` for implementation plans that will execute code changes.

## Execution contract

After human approval, `contextSelection` is immutable with the rest of the plan.

Execution must:

- load selected focused skills/references;
- inspect selected canonical examples first;
- read additional ordinary source files as needed within approved scope;
- not introduce a materially new architecture boundary without replanning.

Reading an additional implementation dependency is normal. Loading a new architecture domain because implementation unexpectedly requires a migration/security/integration change is context drift and normally requires `replan_required`.

## Context drift examples

Not context drift:

- following a method call into another file in the same approved slice;
- inspecting a DTO or test fixture needed to understand the approved behavior;
- reading a project file to confirm an existing dependency.

Context drift requiring re-evaluation:

- a slice-local plan discovers it needs a database migration;
- a business feature unexpectedly changes an integration event contract;
- an endpoint task requires new Keycloak scopes/client configuration;
- a service-local change requires modifying a shared platform library;
- passing tests would require weakening an architecture rule.

## Execution reporting

`execution-result.json` schema `1.1` records `contextCompliance`:

- selected skills loaded;
- selected references loaded;
- canonical examples inspected;
- unapproved architecture context encountered;
- overall compliance flag.

The outer orchestrator should reject a `success` result when policy requires approved context selection but `contextCompliance.compliant` is false.

## Metrics

Persist enough metadata to correlate context strategy with engineering outcomes. Useful measures include:

- selected skill count;
- selected reference count;
- canonical example count;
- context-drift/replan rate;
- Codex input/output token usage when available from the execution platform;
- planning and implementation latency;
- CI first-pass rate;
- review finding rate;
- reverted PR rate.

Do not optimize for minimum tokens alone. The target is the smallest context package that maintains or improves correctness, architecture compliance, and first-pass verification.

## Maintenance

Architecture references should explain project-specific decisions, boundaries, invariants, and canonical examples. Do not duplicate source code or generic .NET tutorials.

When architecture changes intentionally:

1. update executable tests/ADRs/source first as appropriate;
2. update affected agent-context references;
3. update skill routing if the boundary changed;
4. update schema/config only when the machine contract changes;
5. review whether old canonical examples remain representative.