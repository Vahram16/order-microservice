---
name: change-persistence
description: Plan, implement, or review EF Core model, PostgreSQL constraint, query-helper, transaction, schema, or migration changes in an owning microservice. Use when DbContext/model/migration behavior changes. Treat destructive or data-sensitive migrations as high-risk.
---

# Change Persistence

Load:

- the scoped owner document selected by planning;
- `docs/agent-context/architecture/persistence.md`;
- owning `DbContext`, persistence helpers/migrations, and nearest persistence/integration tests.

Also load `concurrency-idempotency.md` only for concurrency tokens, unique race guards, transactions, or retry/reload semantics; load `domain-boundary.md` only when persistence changes reflect a domain invariant. Detailed testing guidance is deferred to `$verify-dotnet-change`.

## Procedure

1. Confirm the data belongs to the approved bounded-context owner.
2. Prefer direct owning-`DbContext` usage in the slice over a new generic repository abstraction.
3. Define exact model/schema impact before generating a migration.
4. Keep domain invariants and database race guards aligned.
5. Use named constraints for intentional provider-conflict translation.
6. Review migration upgrade/data/locking/reversibility implications without inventing production-size assumptions.
7. Preserve Migrator-before-API deployment ordering.
8. Add/update owning persistence/integration tests and verify through `$verify-dotnet-change`.

## Manual/high-risk conditions

Escalate destructive schema operations, data backfills, uniqueness tightening against existing data, cross-service database coupling, uncertain large-table index/lock impact, or concurrency-token changes not explicitly approved.

Never hand-edit the model snapshot as a substitute for a generated migration, and never relocate service-owned schema/migrations into shared persistence infrastructure.