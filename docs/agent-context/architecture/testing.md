# Testing and Verification Strategy

Agent reasoning is not verification. Deterministic commands, tests, analyzers, architecture tests, container checks, and GitHub CI are the evidence.

## Repository baseline

`Directory.Build.props` enforces:

- `net10.0`;
- nullable reference types;
- warnings as errors;
- latest-recommended .NET analysis level;
- .NET analyzers;
- deterministic builds.

Do not suppress warnings merely to make agent-generated code compile.

## CI authority

`.github/workflows/dotnet-ci.yml` is the authoritative pull-request verification path. It currently includes:

- restore of every source/test project;
- Release builds of source projects;
- tests for every test project;
- real PostgreSQL-backed Customer integration coverage;
- a built/run RabbitMQ test image and messaging reliability tests;
- messaging observability JSON/Prometheus validation;
- production Keycloak image build;
- live development realm verification;
- retained diagnostics on failure.

An agent must never say a change is verified merely because it looks correct or because its own reasoning predicts tests will pass.

## Test selection by boundary

Run the narrowest deterministic checks first, then expand to affected boundaries.

### Customer slice / HTTP change

Consider:

- request/validator tests;
- domain tests if invariant behavior changes;
- HTTP integration tests;
- ETag/idempotency/concurrency tests when applicable;
- `CustomerVerticalSliceArchitectureTests`;
- `CustomerDomainBoundaryTests` when domain source changes.

### Persistence change

Consider:

- `CustomerPersistenceModelTests`;
- integration tests using PostgreSQL;
- migration/model snapshot review;
- concurrency/idempotency tests affected by constraints.

### Shared application/primitives

Run their dedicated test projects plus architecture tests for all consumers potentially affected.

### Messaging

Run:

- `Microservices.ArchitectureTests` messaging rules;
- `Microservices.Messaging.Tests`, including real RabbitMQ/PostgreSQL behavior;
- contract serialization/routing/retry/recovery tests relevant to the change.

### Security / Keycloak

Run:

- `Microservices.Security.Tests`;
- Customer identity/authorization integration tests when affected;
- `scripts/verify-keycloak-development.sh` for realm/client configuration changes;
- CI image/smoke path.

### Service defaults / Problem Details

Run `Microservices.ServiceDefaults.Tests` and any affected API integration tests.

## Architecture tests are design constraints

Architecture tests are not low-value implementation details. A failure means one of:

1. the implementation violates an accepted architecture rule; or
2. the architecture rule itself is intentionally changing.

Case 2 requires explicit human approval and corresponding documentation/test changes. Do not delete or relax a guardrail just to get green CI.

## Verification reporting

Execution output must distinguish:

- `passed`: command actually executed with successful exit code;
- `failed`: command executed and failed;
- `not_run`: not executed, with reason;
- `blocked`: environment/dependency prevented execution.

Never convert `not_run` into `passed`.

## Suggested local order

For a normal localized .NET change:

```text
relevant unit/domain tests
    ↓
affected integration/architecture tests
    ↓
Release build of affected project(s)
    ↓
broader affected test project(s)
    ↓
GitHub CI
```

The exact commands should be derived from existing project paths and CI, not guessed.

## Review questions

1. Does every acceptance criterion map to deterministic evidence?
2. Were new invariants given failure-path tests as well as happy-path tests?
3. Were concurrency/idempotency races tested where the change affects them?
4. Did architecture tests remain intact?
5. Did the agent accurately distinguish executed from unexecuted checks?
6. Does CI cover dependencies/infrastructure that the local environment did not?