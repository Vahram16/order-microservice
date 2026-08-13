# Context and Placement Selection Contract

The automation treats repository placement and context as approved execution dependencies, not unbounded agent discretion.

## Goal

Provide enough repository-specific knowledge for correct implementation while avoiding repeated broad discovery, unrelated manuals, or repeated placement inference.

The model is progressive disclosure:

```text
AGENTS.md
   ↓
project-map.md
   ↓
one scoped owner context
   ↓
only affected architecture references
   ↓
1-3 canonical source/test examples
```

`project-map.md` is the small ownership router. `services/` and `platform/` contain scoped owner knowledge. `architecture/` contains behavior/boundary rules. Skills contain repeatable procedures.

## Planning owns placement and context selection

The read-only planning stage emits `projectPlacement` and `contextSelection` in `plan.json` schema `1.2`.

Example:

```json
{
  "projectPlacement": {
    "ownerKind": "bounded-context-service",
    "boundedContext": "Customer",
    "targetProjects": ["src/Services/Customer/Customer.Api/Customer.Api.csproj"],
    "targetFolders": ["src/Services/Customer/Customer.Api/Features/Customers/UpdatingDetails/V1"],
    "testProjects": ["tests/Customer.Api.Tests/Customer.Api.Tests.csproj"],
    "newProjects": [],
    "dependencyChanges": [],
    "placementReasons": ["The behavior is Customer-owned and follows the existing Customer vertical-slice boundary."],
    "sharedAbstractionJustification": null
  },
  "contextSelection": {
    "skills": ["implement-vertical-slice", "change-domain-model", "verify-dotnet-change"],
    "references": [
      "docs/agent-context/services/customer.md",
      "docs/agent-context/architecture/vertical-slice.md",
      "docs/agent-context/architecture/domain-boundary.md"
    ],
    "canonicalExamples": [
      "src/Services/Customer/Customer.Api/Features/Customers/UpdatingDetails/V1/UpdateCustomerDetailsEndpoint.cs",
      "tests/Customer.Api.Tests/CustomerDomainTests.cs"
    ],
    "selectionReasons": ["The task changes one Customer mutation and a domain invariant; persistence, messaging, and identity are not affected."]
  }
}
```

The example is illustrative. Actual ownership, paths, dependencies, and context come from current repository evidence and the task.

## Placement selection rules

1. Start with `project-map.md` to identify the owner category.
2. Load only the matching owner context under `services/` or `platform/`.
3. Verify placement against `Microservices.Boilerplate.slnx`, affected `.csproj`, current source, and tests.
4. Prefer the narrowest owning service/slice over a new shared abstraction.
5. Record exact target projects/folders/test projects.
6. Record every proposed project-reference addition/removal explicitly.
7. A new project/service must appear in `newProjects` and requires explicit approved business/architecture requirements.
8. `sharedAbstractionJustification` must show demonstrated cross-service need whenever new shared behavior is proposed; otherwise keep it `null`.
9. Every `fileChanges` item records an owner consistent with `projectPlacement`.

## Context selection rules

1. Select only the scoped owner context needed for execution. Do not include every service/platform document.
2. Select focused skills by actual affected behavior boundary.
3. Select only architecture references needed for those boundaries.
4. Use `testing-map.md` for test ownership; load `architecture/testing.md` only when detailed verification semantics are needed.
5. Prefer 1-3 nearest canonical source/test examples; `preferredCanonicalExampleLimit` is policy guidance, not a hard correctness limit.
6. Do not select unrelated areas "just in case".
7. Include `verify-dotnet-change` for implementation plans executing code changes.

## Approval contract

After human approval, `projectPlacement`, `fileChanges.owner`, and `contextSelection` are immutable with the plan fingerprint.

Execution must:

- implement in approved owners/projects/folders;
- load selected owner context, focused skills, and behavior references only;
- inspect selected canonical examples first;
- read additional ordinary source files as needed inside approved scope;
- not introduce a materially new owner, project dependency, shared abstraction, or architecture boundary without replanning.

## Normal discovery vs drift

Normal discovery:

- following calls/files inside the approved owner/boundary;
- inspecting a DTO/test fixture needed for approved behavior;
- reading a project file to confirm an existing dependency;
- reading an implementation helper inside approved scope.

Drift requiring re-evaluation:

- slice-local plan discovers a migration is required;
- Customer-only plan discovers another bounded context is the correct owner;
- service-local change needs a new `src/Shared` abstraction;
- new project reference was absent from approved `dependencyChanges`;
- business feature unexpectedly changes durable integration contracts;
- endpoint work requires new Keycloak/client policy;
- service-local task requires shared platform/infrastructure changes;
- API startup would need to execute migrations;
- passing tests would require weakening architecture rules.

These normally return `replan_required` rather than silently relocating code or loading broader context.

## Execution reporting

`execution-result.json` schema `1.2` records independent `contextCompliance` and `placementCompliance`.

Context compliance records selected skills/references/examples and unapproved architecture context. Placement compliance records approved/actual owners, observed project-reference changes, unapproved placements, and overall compliance. Every changed file also reports its owner.

The outer orchestrator must reject `success` when required policy has either compliance dimension false.

## Metrics

Persist enough metadata to evaluate the context strategy empirically:

- selected owner-reference count;
- selected skill/reference/canonical-example count;
- target project/folder count;
- placement/dependency drift rate;
- context-drift/replan rate;
- Codex input/output token usage when available;
- planning/implementation latency;
- CI first-pass rate;
- review finding rate;
- reverted PR rate.

Optimize for the **smallest context package that preserves or improves correctness**, not minimum tokens in isolation.

## Maintenance

When repository structure/architecture changes intentionally:

1. update solution/project files, executable tests, ADRs, and source as appropriate;
2. update `project-map.md` when owner/project topology changes;
3. update only affected service/platform owner documents;
4. update affected behavior references/skill routing;
5. update schemas/config only when the machine contract changes;
6. review canonical examples;
7. run `scripts/verify-agent-context.py` so stale paths/skill/schema routing fail deterministically.