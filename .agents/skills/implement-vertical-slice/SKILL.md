---
name: implement-vertical-slice
description: Plan or implement a business API use case using this repository's pure versioned Vertical Slice Architecture. Use for new or modified Customer-style endpoints, commands/queries, validators, handlers, request/result contracts, or slice-local behavior. Do not use for messaging-only, Keycloak-only, or shared-platform-only changes.
---

# Implement Vertical Slice

Load only:

1. `docs/agent-context/architecture/vertical-slice.md`;
2. `docs/agent-context/architecture/testing.md`;
3. the nearest analogous slice and its tests.

Additionally load:

- `domain-boundary.md` when business invariants/state transitions change;
- `api-and-errors.md` when route/response/error/authorization behavior changes;
- `concurrency-idempotency.md` for mutations using ETags, idempotency, explicit transactions, or race recovery;
- `persistence.md` when EF model/schema/query helpers change;
- `security.md` only when authentication/authorization/identity behavior changes;
- `messaging.md` only when the slice emits/consumes integration messages.

## Procedure

1. Identify the exact owning bounded context and use case.
2. Choose the nearest canonical slice before designing files.
3. Keep the change inside one versioned slice unless a genuinely stable cross-slice primitive is required.
4. Keep HTTP concerns in the endpoint, application orchestration in the handler, and business invariants in Domain.
5. Use shared `ICommand`/`IQuery` contracts rather than raw MediatR request contracts.
6. Preserve existing error, concurrency, idempotency, and authorization semantics that apply to the use case.
7. Add tests at each affected boundary and retain architecture-test compliance.
8. Review the final diff for sibling-slice coupling, speculative abstractions, or scope drift.

## Stop conditions

Stop/replan rather than improvising if:

- acceptance criteria do not define a needed domain rule;
- implementation requires a new cross-service/shared abstraction not in the approved plan;
- a durable API/integration/security contract must change unexpectedly;
- a schema migration becomes necessary but was not approved;
- an architecture test must be weakened to proceed.

## Canonical starting points

- read: `GettingCurrent/V1/`;
- ordinary mutation: `UpdatingDetails/V1/`;
- idempotent transactional mutation: `AddingAddress/V1/`;
- owned-child update/delete: `UpdatingAddress/V1/`, `RemovingAddress/V1/`;
- lifecycle/destructive business operation: `ClosingAccount/V1/`.

Do not copy Customer business semantics into a different bounded context.