# Repository Agent Guide

This is a production-oriented .NET 10 microservices repository. Preserve architecture enforced by source, tests, CI, project files, and accepted ADRs. Do not invent business requirements, bounded contexts, aggregates, endpoints, authorization rules, integration contracts, or infrastructure absent from the approved task and repository evidence.

## Context strategy

Use progressive disclosure. Keep the always-loaded context small.

1. Use `docs/agent-context/project-map.md` only to identify the owning service/platform area.
2. Load the matching scoped owner document under `docs/agent-context/services/` or `docs/agent-context/platform/`.
3. Use `docs/agent-context/README.md` and the narrowest applicable skill to select behavior-specific architecture references.
4. Inspect the nearest canonical production example, owning `.csproj`, and relevant tests before designing a new pattern.
5. Load deeper ADRs/service docs only for boundaries actually affected.

Do **not** preload every service, platform, architecture, or testing document. Owner documents answer **where code belongs**. Architecture references answer **how the affected behavior must work**. Skills define **how to perform a repeatable workflow**.

Evidence priority when guidance conflicts:

1. approved acceptance criteria/scope;
2. executable architecture/security/integration tests;
3. accepted ADRs and service documentation;
4. current production implementation and project files;
5. agent-context notes;
6. generic framework convention.

If the first four materially disagree, stop and surface the conflict.

## Global invariants

- Each service owns its business data, `DbContext`, schema, and migrations.
- API processes do not apply migrations at startup; service Migrators own deployment-time migration execution.
- Business services use pure versioned vertical slices and shared `ICommand`/`IQuery` contracts.
- Do not introduce generic repository/application-service layers merely to hide EF Core or reshape vertical slices.
- Domain source owns business invariants and remains free of HTTP, EF Core, MediatR, FluentValidation, database-provider, security-plumbing, and transport dependencies.
- Application/domain messaging uses approved transport-independent event/command abstractions; MassTransit/RabbitMQ details stay in infrastructure.
- Preserve explicit concurrency, idempotency, transaction, error, security, and compatibility semantics demonstrated by the owning service.
- Keycloak owns identity-provider responsibilities; resource APIs own token validation, least-privilege authorization, resource ownership, and domain authorization.
- Architecture tests are executable design constraints, not tests to weaken for convenience.
- Shared code is earned by demonstrated stable cross-service reuse. Do not move service-local behavior into `src/Shared` speculatively.
- A planned owner/project/folder and project-reference change is part of approved architecture scope. Material placement/dependency drift requires replanning.

`Customer.Api` is the concrete business-service reference. `ServiceTemplate` is infrastructure scaffolding. Derive structural conventions from Customer but never copy its business semantics into another bounded context.

## Engineering protocol

1. Treat the Jira issue and approved plan as the scope boundary.
2. Identify owner via `project-map.md`, then load only that owner's scoped context.
3. Inspect the owning `.csproj`, nearest analogous implementation, and relevant tests before proposing files or dependencies.
4. Plan before workspace writes when the workflow requires approval.
5. State assumptions, blocking questions, affected boundaries, compatibility/dependency impact, placement rationale, and risk.
6. After approval, implement the smallest coherent change in the approved owner/location.
7. Add tests at every affected architectural boundary.
8. Run deterministic verification; an agent statement is never test evidence.
9. Review the final diff against the approved plan, owner placement, dependency changes, and repository invariants.
10. If correct implementation requires material unapproved scope, another owner/project, a new shared dependency, a new bounded context, or a new architecture boundary, return to planning instead of silently expanding work.

## Verification

`.github/workflows/dotnet-ci.yml` is final pull-request verification authority. The repository enforces nullable reference types, latest-recommended analyzers, warnings-as-errors, and deterministic builds.

Use the repository verification skill and `docs/agent-context/testing-map.md` to choose affected checks. Load `docs/agent-context/architecture/testing.md` only when detailed verification semantics are needed. A check is `passed` only when its command actually executed successfully.

## Autonomous limits

Unless an explicit human-approved workflow says otherwise, an agent must not:

- merge a pull request or deploy an environment;
- modify/reveal production secrets;
- perform destructive production/database operations;
- weaken authentication/authorization/security controls;
- make unapproved breaking public/integration-contract changes;
- introduce new cross-service/shared abstractions without demonstrated need;
- expand an approved task into unrelated refactoring;
- create a new bounded context/service/domain from repository naming or template examples.

Escalate these as high-risk/manual work.

Use the narrowest applicable repository skill from `.agents/skills`. Use `$order-microservice-architecture` only when owner or architecture classification is unclear. Orchestration-only Jira/approval/review skills are intended for explicit workflow invocation rather than ordinary feature-task matching.

See `docs/agent-context/README.md` for context routing and `docs/agentic-automation.md` for orchestration/state/approval contracts.