# Repository Agent Guide

This is a production-oriented .NET 10 microservices repository. Preserve architecture already enforced by source, tests, CI, and accepted ADRs. Do not invent business requirements, aggregates, endpoints, authorization rules, integration contracts, or infrastructure that are not supported by the approved task and repository evidence.

## Context strategy

Keep context selective.

1. Read `docs/agent-context/README.md` to route architecture knowledge.
2. Use the narrowest matching skill under `.agents/skills/`.
3. Inspect the nearest canonical production example and its tests.
4. Load deeper service docs/ADRs only for boundaries actually affected.

Do not preload every architecture document for every task.

Evidence priority when guidance conflicts:

1. approved acceptance criteria/scope;
2. executable architecture/security/integration tests;
3. accepted ADRs and service documentation;
4. current production implementation;
5. agent-context notes;
6. generic framework convention.

If the first four materially disagree, stop and surface the conflict.

## Global invariants

- Each service owns its business data and `DbContext`.
- API processes do not apply migrations at startup; service Migrators own deployment-time migration execution.
- Business services use pure versioned vertical slices and shared `ICommand`/`IQuery` contracts.
- Do not introduce generic repository/application-service layers merely to hide EF Core or reshape vertical slices.
- Domain source owns business invariants and remains free of HTTP, EF Core, MediatR, FluentValidation, database-provider, security-plumbing, and transport dependencies.
- Application/domain messaging uses approved transport-independent event/command abstractions; MassTransit/RabbitMQ details stay in infrastructure.
- Preserve explicit concurrency, idempotency, transaction, error, security, and compatibility semantics demonstrated by the owning service.
- Keycloak owns identity-provider responsibilities; resource APIs own token validation, least-privilege authorization, resource ownership, and domain authorization.
- Treat architecture tests as executable design constraints, not tests to weaken for convenience.

`Customer.Api` is the concrete business-service reference. `ServiceTemplate` is infrastructure scaffolding. Derive structural conventions from Customer but never copy its business semantics into another bounded context.

## Engineering protocol

1. Treat the Jira issue and approved plan as the scope boundary.
2. Inspect the nearest analogous implementation/tests before designing a new pattern.
3. Plan before workspace writes when the workflow requires approval.
4. State assumptions, blocking questions, affected boundaries, compatibility impact, and risk.
5. After approval, implement the smallest coherent change.
6. Add tests at every affected architectural boundary.
7. Run deterministic verification; never treat an agent statement as test evidence.
8. Review the final diff against the approved plan and repository invariants.
9. If implementation requires material unapproved scope, return to planning instead of silently expanding work.

## Verification

`.github/workflows/dotnet-ci.yml` is the final pull-request authority. The repository builds with nullable reference types, latest-recommended analyzers, warnings-as-errors, and deterministic builds.

Use `$verify-dotnet-change` after implementation to select real affected checks. A check is `passed` only when its command actually executed successfully.

## Autonomous limits

Unless an explicit human-approved workflow says otherwise, an agent must not:

- merge a pull request;
- deploy to any environment;
- modify/reveal production secrets;
- perform destructive production/database operations;
- weaken authentication/authorization/security controls;
- make breaking public/integration-contract changes;
- introduce new cross-service/shared abstractions without demonstrated need;
- expand an approved task into unrelated refactoring.

Escalate these as high-risk/manual work.

## Skills

Architecture/context:

- `$order-microservice-architecture` — route unknown/cross-cutting work to focused context.
- `$implement-vertical-slice` — business endpoints/use cases.
- `$change-domain-model` — aggregates/value objects/invariants.
- `$change-persistence` — EF Core/schema/migrations/transactions.
- `$change-messaging` — events/commands/outbox/retry/topology.
- `$change-security` — Keycloak/token/claims/scopes/roles/authorization.
- `$verify-dotnet-change` — deterministic verification selection/reporting.

Automation flow:

- `$jira-work-intake` — read-only Jira discovery/eligibility.
- `$jira-implementation-plan` — read-only structured planning.
- `$approved-plan-implementation` — exact approved-plan workspace-write execution.
- `$pr-review` — architecture/correctness/security/evidence review.
- `$pr-feedback-fix` — bounded accepted-review fixes.

See `docs/agentic-automation.md` for orchestration/state/approval contracts.