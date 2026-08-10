# Context and Placement Selection Contract

The automation treats both repository placement and context as approved execution dependencies, not as unbounded agent discretion.

## Goal

Provide enough repository-specific knowledge for correct implementation while avoiding repeated broad repository discovery, loading unrelated architecture manuals, or repeatedly guessing where code belongs.

This optimizes quality and token/cycle-time efficiency. It does not assume that more documentation automatically means fewer tokens.

`docs/agent-context/project-structure.md` is the ownership/placement source for agents. Focused files under `architecture/` describe behavioral rules.

## Planning owns placement and context selection

The read-only planning stage emits both `projectPlacement` and `contextSelection` in `plan.json` schema `1.2`.

Example placement:

```json
{
  "projectPlacement": {
    "ownerKind": "bounded-context-service",
    "boundedContext": "Customer",
    "targetProjects": [
      "src/Services/Customer/Customer.Api/Customer.Api.csproj"
    ],
    "targetFolders": [
      "src/Services/Customer/Customer.Api/Features/Customers/UpdatingDetails/V1"
    ],
    "testProjects": [
      "tests/Customer.Api.Tests/Customer.Api.Tests.csproj"
    ],
    "newProjects": [],
    "dependencyChanges": [],
    "placementReasons": [
      "The behavior is a Customer-owned mutation and follows the existing Customer vertical-slice boundary."
    ],
    "sharedAbstractionJustification": null
  }
}
```

Example context selection:

```json
{
  "contextSelection": {
    "skills": [
      "implement-vertical-slice",
      "change-domain-model",
      "verify-dotnet-change"
    ],
    "references": [
      "docs/agent-context/project-structure.md",
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
}
```

Examples are illustrative only; the planner must derive actual ownership, paths, dependencies, and context from the task/repository.

## Placement selection rules

1. Read `project-structure.md`, the solution file, and affected `.csproj` files before assigning ownership.
2. Identify the bounded context or shared/platform owner before proposing files.
3. Prefer the narrowest owning service/slice over a new shared abstraction.
4. Record exact target projects/folders and corresponding test projects.
5. Record every proposed project-reference addition/removal explicitly.
6. A new project/service must appear in `newProjects` and requires explicit requirements/approval.
7. `sharedAbstractionJustification` must explain demonstrated cross-service need whenever the plan introduces new shared behavior; otherwise keep it `null`.
8. Every `fileChanges` item records an `owner` consistent with `projectPlacement`.

## Context selection rules

1. Start from `AGENTS.md`, `project-structure.md`, and the architecture router.
2. Select focused skills by actual affected behavior boundary.
3. Select architecture references needed to understand those boundaries.
4. Include `project-structure.md` in execution context when placement/dependency decisions remain material during implementation.
5. Prefer 1-3 nearest canonical source/test examples; `preferredCanonicalExampleLimit` is configurable policy, not a hard correctness limit.
6. Do not select unrelated areas "just in case".
7. Include `verify-dotnet-change` for implementation plans that will execute code changes.

## Approval contract

After human approval, `projectPlacement`, `fileChanges.owner`, and `contextSelection` are immutable with the rest of the plan fingerprint.

Execution must:

- implement in approved owners/projects/folders;
- load selected focused skills/references;
- inspect selected canonical examples first;
- read additional ordinary source files as needed inside approved scope;
- not introduce a materially new owner, dependency, shared abstraction, or architecture boundary without replanning.

## Normal discovery vs placement/context drift

Normal discovery:

- following a method call into another file in the same approved service/project;
- inspecting a DTO or test fixture needed to understand approved behavior;
- reading a project file to confirm an existing dependency;
- reading an implementation helper inside the approved architectural boundary.

Placement/context drift requiring re-evaluation:

- a slice-local plan discovers it needs a database migration;
- a Customer-only plan discovers the correct owner is another bounded context;
- a service-local change needs a new `src/Shared` abstraction;
- a new project reference is required but was absent from `projectPlacement.dependencyChanges`;
- a business feature unexpectedly changes an integration event contract;
- an endpoint task requires new Keycloak scopes/client configuration;
- a service-local change requires modifying a shared platform library;
- API startup would need to run migrations instead of its Migrator;
- passing tests would require weakening an architecture rule.

These normally require `replan_required` rather than silent relocation or broader context loading.

## Execution reporting

`execution-result.json` schema `1.2` records two independent compliance dimensions.

`contextCompliance` records:

- selected skills loaded;
- selected references loaded;
- canonical examples inspected;
- unapproved architecture context encountered;
- overall context compliance.

`placementCompliance` records:

- approved owners;
- actual owners touched by the diff;
- observed project-reference additions/removals and whether each was approved;
- unapproved file placements;
- overall placement compliance.

Every changed file also reports its `owner`.

The outer orchestrator must reject a `success` result when required policy has either `contextCompliance.compliant == false` or `placementCompliance.compliant == false`.

## Metrics

Persist enough metadata to correlate context/placement strategy with engineering outcomes. Useful measures include:

- selected skill count;
- selected reference count;
- canonical example count;
- target project/folder count;
- unapproved placement rate;
- unexpected project-dependency rate;
- context-drift/replan rate;
- Codex input/output token usage when available from the execution platform;
- planning and implementation latency;
- CI first-pass rate;
- review finding rate;
- reverted PR rate.

Do not optimize for minimum tokens alone. The target is the smallest placement-aware context package that maintains or improves correctness, architecture compliance, and first-pass verification.

## Maintenance

`project-structure.md` explains current ownership, project/folder responsibilities, dependency directions, tests, and code-placement rules. Architecture references explain project-specific behavioral decisions and invariants. Do not duplicate source code or generic .NET tutorials.

When the repository structure or architecture changes intentionally:

1. update solution/project files, executable tests, ADRs, and source as appropriate;
2. update `project-structure.md` when ownership/project topology changes;
3. update affected architecture references;
4. update skill routing if boundaries changed;
5. update schemas/config only when the machine contract changes;
6. review whether old canonical examples remain representative.