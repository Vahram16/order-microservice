# Product Bounded Context

Use this document only for work owned by `src/Services/Product`.

## Ownership

`Product.Api` owns catalog behavior, the Product aggregate, HTTP contracts, persistence/schema/migrations, and publication of Product-owned catalog facts. It does not own Customer, Order, Inventory, or Payment state.

Business use cases remain pure versioned slices under `Features/Products/<UseCase>/V1/`; Product invariants remain framework-free under `Domain`.

`ProductDbContext` and migrations stay under Product. `Product.Migrator` runs before Product API rollout; API startup never applies migrations.

## Catalog integration

Create, update, and delete operations publish `ProductCatalogChanged` through `IIntegrationEventPublisher` in the same Product `DbContext` persistence boundary as the catalog mutation. The MassTransit PostgreSQL bus outbox therefore closes the database-commit/message-publication gap.

The event is a Product-owned fact containing the ProductId, display/catalog snapshot needed by consumers, Product version, availability flag, and occurrence time. Product does not direct the event to Order and does not depend on Order. Consumers independently maintain any service-local projections they require.

Delete publishes an unavailable catalog fact in the same transaction that removes Product state. Existing orders keep their own historical item snapshots and are not rewritten by later Product changes.

## Runtime boundaries

Product uses RabbitMQ/MassTransit only through repository messaging abstractions/composition. Product domain/features do not depend on RabbitMQ or MassTransit types. The API remains an authenticated resource API and owns its catalog authorization.

## Tests

Primary owner: `tests/Product.Api.Tests`; repository-wide dependency/messaging rules remain under `tests/Microservices.ArchitectureTests` and messaging-platform tests.

## Context routing

- business slice -> `../architecture/vertical-slice.md`;
- domain -> `../architecture/domain-boundary.md`;
- API/errors -> `../architecture/api-and-errors.md`;
- EF/migration -> `../architecture/persistence.md`;
- catalog event/outbox -> `../architecture/messaging.md`;
- security -> `../architecture/security.md`;
- verification -> `../testing-map.md`.
