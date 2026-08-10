---
name: implement-vertical-slice
description: Plan or implement a business API use case using this repository's pure versioned Vertical Slice Architecture. Use for new or modified Customer-style endpoints, commands/queries, validators, handlers, request/result contracts, or slice-local behavior. Do not use for messaging-only, Keycloak-only, or shared-platform-only changes.
---

# Implement Vertical Slice

Load first:

1. the scoped owner document selected by planning (for Customer: `docs/agent-context/services/customer.md`);
2. `docs/agent-context/architecture/vertical-slice.md`;
3. the nearest analogous slice and its tests.

Additionally load only when affected:

- `domain-boundary.md` for business invariants/state transitions;
- `api-and-errors.md` for route/response/error/authorization behavior;
- `concurrency-idempotency.md` for ETags, idempotency, explicit transactions, or race recovery;
- `persistence.md` for EF model/schema/query-helper changes;
- `security.md` for authentication/authorization/identity behavior;
- `messaging.md` for integration messages.

Do not load the detailed testing manual during design by default. Test ownership comes from `testing-map.md`; `$verify-dotnet-change` loads `architecture/testing.md` when verification is executed.

## Procedure

1. Identify the exact owning bounded context/use case and approved placement.
2. Choose the nearest canonical slice before designing files.
3. Keep the change inside one versioned slice unless a stable cross-slice primitive is justified.
4. Keep HTTP concerns in the endpoint, application orchestration in the handler, and business invariants in Domain.
5. Use shared `ICommand`/`IQuery` contracts rather than raw MediatR request contracts.
6. Preserve applicable error, concurrency, idempotency, persistence, and authorization semantics.
7. Add/update tests in the owning test project at each affected boundary.
8. Review the diff for sibling-slice coupling, speculative abstractions, placement drift, or scope expansion.

## Stop conditions

Stop/replan if requirements do not define a needed business rule, a new shared/cross-service abstraction is required, a durable API/integration/security contract changes unexpectedly, an unapproved migration appears, placement changes materially, or an architecture test would need weakening.

## Canonical starting points

For Customer work use the paths in `services/customer.md`, including `GettingCurrent/V1`, `UpdatingDetails/V1`, `AddingAddress/V1`, `UpdatingAddress/V1`, `RemovingAddress/V1`, and `ClosingAccount/V1` as behavior-matched examples.

Do not copy Customer business semantics into another bounded context.