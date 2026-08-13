# Persistence and Migration Boundary

Each business service owns its database model and `DbContext`. Customer is the concrete reference.

## Ownership

- `Customer.Api` owns `CustomerDbContext` and the Customer database schema.
- Persistence configuration lives with the owning service, not in a cross-service generic repository layer.
- API replicas do not run migrations during startup.
- `Customer.Migrator` is the run-once deployment migration boundary.

Do not introduce a generic repository/unit-of-work abstraction merely to hide EF Core. Existing vertical-slice handlers may use the owning `DbContext` directly.

## EF Core model

`CustomerDbContext.cs` shows the current conventions:

- explicit table/key configuration;
- bounded lengths and requiredness;
- explicit value conversions;
- `Customer.Version` configured as a concurrency token;
- named unique constraints/indexes for business race guards;
- aggregate-owned address relationship with cascade deletion;
- audit relationship with restricted deletion;
- filtered unique indexes for one default shipping/billing address per customer.

The domain remains authoritative for business invariants; database constraints duplicate selected invariants to close concurrency races.

## Schema changes

A schema change requires all of:

1. explicit requirement tied to the owning bounded context;
2. model configuration change;
3. generated/reviewed migration in the owning service;
4. migration compatibility review (upgrade path, locking/data impact, reversibility where relevant);
5. owning service persistence/integration tests;
6. deployment ordering that runs the Migrator before API rollout.

Never hand-edit an EF model snapshot to simulate a migration.

## Migration risk

Treat these as high-risk for autonomous execution unless explicitly approved:

- destructive column/table/index changes;
- required-column introduction without safe data strategy;
- data backfills;
- uniqueness changes against existing data;
- concurrency-token changes;
- large/index-building operations with unknown production impact;
- cross-service/shared database coupling.

Do not invent production data volumes or safe lock duration. Surface unknown operational assumptions.

## Query/persistence helpers

Stable cross-slice persistence behavior may live under `Persistence` (for example composable Customer query extensions, audit persistence helpers, default-address persistence, named database constraints). Keep helpers narrow and persistence-specific; do not turn them into a horizontal business-service layer.

## Exception translation

Translate provider/database exceptions only when the repository has an explicit, testable semantic mapping. Match named constraints rather than parsing arbitrary human-readable exception text.

Unknown database failures follow the safe unhandled-error path.

## Canonical evidence

- `src/Services/Customer/Customer.Api/Persistence/CustomerDbContext.cs`;
- `CustomerDatabaseConstraints.cs`;
- `CustomerQueryExtensions.cs`;
- `CustomerAddressPersistence.cs`;
- `CustomerAuditExtensions.cs`;
- `PostgresExceptionExtensions.cs`;
- `src/Services/Customer/Customer.Migrator/Program.cs`;
- Customer EF migrations;
- `tests/Customer.Api.Tests/CustomerPersistenceModelTests.cs`;
- `CustomerApiIntegrationTests.cs`;
- `docs/customer-service.md`.

## Review questions

1. Does this data belong to this bounded context?
2. Is a new abstraction actually required, or can the slice use the owning `DbContext` directly?
3. Are domain and database invariants aligned?
4. Is concurrency still enforced at the database boundary?
5. Is the migration deployable before the API version that depends on it?
6. Could the migration lock/rewrite a large table or invalidate existing data?
7. Are known database conflicts translated deterministically and unknown failures left safe?