---
name: change-persistence
description: Plan, implement, or review EF Core model, PostgreSQL constraint, query-helper, transaction, schema, or migration changes in an owning microservice. Use when DbContext/model/migration behavior changes. Treat destructive or data-sensitive migrations as high-risk.
---

# Change Persistence

Load:

- `docs/agent-context/architecture/persistence.md`;
- `docs/agent-context/architecture/testing.md`;
- owning service `DbContext`, persistence helpers, migrations, and persistence/integration tests.

Also load:

- `concurrency-idempotency.md` when concurrency tokens, unique race guards, transactions, or retry/reload semantics are affected;
- `domain-boundary.md` when persistence changes reflect a domain invariant.

## Procedure

1. Confirm the data belongs to the owning bounded context.
2. Prefer direct owning-`DbContext` usage in the slice over a new generic repository abstraction.
3. Define exact model/schema impact before generating a migration.
4. Keep domain invariants and database race guards aligned.
5. Use named constraints for intentional provider-conflict translation.
6. Review migration upgrade/data/locking/reversibility implications; do not invent production-size assumptions.
7. Preserve Migrator-before-API deployment ordering.
8. Add/update persistence and integration tests.

## Manual/high-risk conditions

Escalate when the change includes destructive schema operations, data backfills, uniqueness tightening against existing data, cross-service database coupling, uncertain large-table index/lock impact, or concurrency-token changes not explicitly approved.

Never hand-edit the model snapshot as a substitute for a generated migration.