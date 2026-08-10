# Repository Agent Guide

This repository is a production-oriented .NET 10 microservices foundation. Preserve the architecture already enforced by source, tests, CI, and ADRs. Do not invent business requirements, aggregates, endpoints, authorization rules, integration contracts, or infrastructure that are not supported by the task and repository evidence.

## Context routing

Read only the context needed for the task:

- `README.md` for the repository/service baseline.
- `docs/customer-service.md` when working on Customer or when using Customer as the reference vertical-slice implementation.
- `docs/error-handling.md` for API error contracts and Problem Details.
- `docs/keycloak-integration.md` for authentication, token validation, scopes, roles, and identity boundaries.
- `docs/adr/` and `docs/messaging-failure-delivery-policy.md` for messaging, outbox/inbox, routing, retry, delivery, and contract decisions.
- `tests/Customer.Api.Tests/CustomerVerticalSliceArchitectureTests.cs` and `CustomerDomainBoundaryTests.cs` for executable Customer architecture rules.
- `.github/workflows/dotnet-ci.yml` for the authoritative CI verification path.

Prefer neighboring production code and executable architecture tests over generic patterns.

## Architectural invariants

### Service and persistence boundaries

- A service owns its business data and `DbContext`.
- API processes do not apply migrations at startup. A service migrator is the deployment-time migration boundary.
- Do not introduce a repository facade or generic application-service layer merely to hide EF Core. Existing vertical-slice handlers may use the service `DbContext` directly.
- Keep infrastructure and persistence concerns out of domain source.

### Pure vertical slices

For Customer-style business APIs, organize use cases as versioned vertical slices:

`Features/<Area>/<UseCase>/V1/`

A slice owns its endpoint, request/response contracts when needed, command/query, validator, handler, authorization behavior, and HTTP mapping. Follow the nearest analogous slice before introducing a new pattern.

- One top-level responsibility per source file.
- Endpoints expose a static `Map(...)` method and are composed by route-builder extensions.
- Commands implement the shared `ICommand<TResponse>` contract; queries implement `IQuery<TResponse>`.
- Handlers implement the corresponding shared command/query handler contracts.
- Do not use raw `IRequest`/`IRequestHandler` inside business slices.
- Do not reference sibling use-case namespaces from a slice.
- Share only stable cross-slice primitives under an explicit `Common` boundary.
- Do not create monolithic `*Slice.cs` files.

### Domain boundary

Domain code contains business invariants and remains framework-free. Do not reference ASP.NET Core, EF Core, MediatR, FluentValidation, Npgsql, application features, persistence, infrastructure, security plumbing, or service-default plumbing from domain source.

Domain errors describe domain semantics. Application handlers own aggregate absence, identity extraction, request preconditions, idempotency interpretation, and known persistence conflicts. Presentation maps approved semantic errors to HTTP Problem Details.

### Concurrency, idempotency, and transactions

Preserve existing optimistic-concurrency and idempotency semantics. Do not weaken ETag/precondition behavior, database concurrency tokens, unique constraints, or retry-safe outcomes. When a use case requires multiple persistence effects that form one business operation, keep them in one explicit transactional boundary and retain failure atomicity.

### Messaging

- Application/domain code must remain transport-independent.
- Publish integration events through `IIntegrationEventPublisher` and send owned commands through `IIntegrationCommandSender<TCommand>`.
- Do not inject MassTransit or RabbitMQ abstractions into application/domain code.
- Preserve PostgreSQL bus outbox and consumer inbox/outbox semantics.
- Treat endpoint names and integration-contract compatibility as durable architecture.
- Do not change retry/redelivery, queue durability, or failure-retention policy without reading the accepted ADRs and adding/adjusting verification.

### Security and identity

Keycloak owns authentication, credentials, sessions, and token issuance. Resource APIs own audience/authorized-party validation, scopes, roles, tenant/resource ownership, and domain authorization. Never derive trusted identity from request bodies or routes when the validated token is authoritative.

Do not weaken least-privilege scopes, role boundaries, exact redirect/authorized-party configuration, or safe error disclosure.

## Engineering protocol

1. Treat the Jira issue/approved task as the scope boundary.
2. Inspect the nearest analogous implementation and relevant tests before proposing changes.
3. Produce a plan before editing when the agentic workflow requires approval.
4. Explicitly list assumptions, unresolved questions, architecture impact, migration impact, security impact, and risk.
5. After approval, implement the smallest coherent change that satisfies acceptance criteria.
6. Add or update tests at the same architectural level as the behavior being changed.
7. Run deterministic verification; do not treat an agent statement that tests pass as evidence.
8. Review the final diff against the approved plan and these invariants.

## Verification baseline

The pull-request CI in `.github/workflows/dotnet-ci.yml` is authoritative. At minimum, local changes should be compatible with:

```bash
dotnet restore <project>
dotnet build <project> --configuration Release --no-restore
dotnet test <test-project> --configuration Release --no-restore
```

Run the narrowest relevant tests first, then the broader affected test projects. Changes touching messaging, Keycloak, shared libraries, persistence behavior, or architecture boundaries require their dedicated tests and CI path.

The repository treats compiler/analyzer warnings as errors. Do not suppress warnings to make a change pass unless the suppression itself is justified and scoped.

## Autonomous-operation limits

Unless an explicit human-approved workflow says otherwise, an agent must not:

- merge a pull request;
- deploy to any environment;
- modify or reveal production secrets;
- perform destructive production/database operations;
- weaken authentication/authorization/security controls;
- make a breaking public/integration-contract change;
- introduce a new cross-service/shared abstraction without demonstrated multi-consumer need;
- expand an approved Jira task into unrelated refactoring.

Escalate these as manual-only or high-risk work.

## Agentic workflow skills

Use repository skills under `.agents/skills/`:

- `$order-microservice-architecture` for architecture/context routing.
- `$jira-work-intake` for read-only Jira discovery and eligibility normalization.
- `$jira-implementation-plan` for a read-only, structured implementation plan.
- `$approved-plan-implementation` only after a plan is explicitly approved.
- `$pr-review` for plan/architecture/security/test review of a completed diff.
- `$pr-feedback-fix` for bounded follow-up on accepted PR review feedback.

See `docs/agentic-automation.md` for the state machine, approval gates, MCP setup, schemas, and orchestration contract.