---
name: jira-implementation-plan
description: Produce a read-only, architecture-aware implementation plan for one Jira issue in this repository. Use after Jira intake marks an issue ready for planning and before source edits; the plan identifies the owning project/folder, selects exact skills/references/canonical examples, and conforms to `.automation/schemas/plan.schema.json`.
---

# Jira Implementation Plan

Planning is read-only. Do not edit source, create migrations, change Jira, commit, push, or create a pull request.

Start with `AGENTS.md`, `docs/agent-context/project-structure.md`, and `$order-microservice-architecture` only long enough to determine ownership and classify the task. Then load the smallest focused behavior context needed.

## Required inputs

- exact Jira issue key;
- issue context from Jira intake or permission to read it through Atlassian MCP;
- repository base revision/branch supplied by the orchestrator (normally `develop`).

## Procedure

1. Re-read the issue and acceptance criteria. Treat them as the scope boundary.
2. Use `docs/agent-context/project-structure.md`, `Microservices.Boilerplate.slnx`, and the relevant `.csproj` to identify:
   - owning bounded context/service/shared concern;
   - target project(s);
   - target folder(s);
   - existing direct dependency direction;
   - corresponding test project(s);
   - whether any proposed `src/Shared`, AppHost, infrastructure, or Migrator change is actually justified.
3. Record the placement rationale. Do not choose a folder merely because its name seems convenient.
4. Classify architecture impact.
5. Select focused skills from:
   - `implement-vertical-slice`;
   - `change-domain-model`;
   - `change-persistence`;
   - `change-messaging`;
   - `change-security`;
   - `verify-dotnet-change`.
6. Select only the architecture references required by those impacts from `docs/agent-context/`. Include `docs/agent-context/project-structure.md` in `contextSelection.references` when execution will create files/projects, change project references, cross project/service boundaries, or when placement itself is material to approval.
7. Select 1-3 canonical production/test examples that most closely match the requested behavior. Prefer exact neighboring slices/tests over broad repository exploration.
8. Read the selected context and trace the requested change through actual affected boundaries.
9. Identify exact or best-current candidate files to create/modify. Every planned file must have an owning project/folder reason. If a path is uncertain, say so rather than inventing it.
10. Map every material acceptance criterion to implementation evidence and deterministic verification evidence.
11. Evaluate bounded-context ownership, VSA independence, domain invariants/failure atomicity, concurrency/idempotency/transactions, persistence/migrations, integration contracts/messaging, security/authorization, backward compatibility, observability, deployment, and project dependency impact only where applicable.
12. Classify risk and decide `ready`, `blocked`, or `manual_only`.
13. Produce output conforming to `.automation/schemas/plan.schema.json` (schema version `1.1`) when structured output is requested.

## Project-placement rules

Use the repository map, not generic layered-architecture assumptions.

- Customer business use cases -> `Customer.Api/Features/Customers/<UseCase>/V1`.
- Customer business invariants/value objects -> `Customer.Api/Domain`.
- Customer schema/EF model/migrations -> `Customer.Api/Persistence`; migration execution -> `Customer.Migrator`.
- Service-local infrastructure -> owning service `Infrastructure` only when it is stable service-wide behavior; otherwise keep it with the owning slice.
- Reusable application CQRS/boundaries -> `Microservices.Application` only when truly cross-service.
- Durable cross-service message contracts -> `Microservices.Contracts`.
- MassTransit/RabbitMQ/outbox implementation -> `Microservices.Messaging`.
- Reusable Npgsql/EF-provider behavior -> `Microservices.Persistence.Postgres`.
- Framework-free result/error primitives -> `Microservices.Primitives`.
- Shared JWT/scope/role plumbing -> `Microservices.Security`; domain/resource authorization remains service-owned.
- Shared observability/health/OpenAPI/platform defaults -> `Microservices.ServiceDefaults`.
- Local development orchestration -> AppHost; never production business logic.
- New bounded context/service -> only when requirements explicitly define it; never infer an Order service from repository naming.

A proposal to add a new shared dependency or project is an architecture decision and cannot be hidden inside a normal slice plan.

## Context-selection contract

The plan's `contextSelection` is part of the approval artifact, not informational prose.

It must contain:

- `skills`: only focused skills needed for execution/verification;
- `references`: exact repository reference paths to load, including the project structure map when placement/dependencies are material;
- `canonicalExamples`: exact source/test paths execution should inspect first;
- `selectionReasons`: concise explanation of why each context group is needed and, when relevant, why the target project/folder owns the change.

Do not include every architecture skill/reference by default. The objective is enough context to execute correctly without repeated repository rediscovery.

Examples:

- validator-only Customer change -> existing Customer slice location; `implement-vertical-slice`, `verify-dotnet-change`; vertical-slice/testing references; nearest validator/tests;
- new Customer mutation -> explicit `Customer.Api/Features/Customers/<UseCase>/V1` placement; focused VSA/API/concurrency/domain/testing context as applicable;
- Customer mutation changing invariant and schema -> add `change-domain-model`, `change-persistence`; include project structure when new persistence files/migrations are planned;
- messaging infrastructure change -> confirm `Microservices.Messaging` ownership; `change-messaging`, `verify-dotnet-change`; messaging/testing references; no Customer HTTP context unless actually affected;
- Keycloak authorization change -> first distinguish `Microservices.Security`, service-owned authorization, AppHost development realm, and `infrastructure/keycloak`; then select the security/testing context appropriate to the actual owner.

## Risk guidance

- `low`: localized, reversible, no durable boundary/dependency change;
- `medium`: normal business behavior touching established persistence/concurrency/API semantics;
- `high`: security, migration, durable integration contract, new shared dependency, shared-platform, cross-service behavior, or hard-to-reverse change;
- `critical`: destructive production/data/security operation or unbounded blast radius.

Choose:

- `ready`: implementable after human approval;
- `blocked`: unresolved requirement/dependency/ownership prevents a reliable implementation plan;
- `manual_only`: automation must not execute the change.

## Plan quality rules

A principal-level plan explains why each change belongs in its proposed project/folder, why existing dependency directions remain valid, and why the selected context is sufficient.

Do not:

- invent Order aggregates/business rules absent from Jira/repository evidence;
- invent a service/shared abstraction because the repository has a template;
- load broad context merely because it exists;
- introduce generic repositories/application services to make vertical slices look layered;
- hide concurrency/idempotency/migration/security/contract/dependency implications;
- classify security/durable contract/destructive persistence/new shared-platform changes as low risk;
- claim verification that has not executed.

## Approval boundary

The complete plan, including target file/project placement and `contextSelection`, is immutable after approval. Execution receives the exact approved plan plus orchestrator-generated fingerprint.

If implementation discovers that correct work belongs in a materially different project/bounded context, needs a new project reference/shared abstraction, or requires a new architecture boundary, return `replan_required` instead of silently relocating code or expanding context.