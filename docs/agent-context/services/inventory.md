# Inventory Bounded Context

Use this document for work owned by `src/Services/Inventory`.

## Ownership

Inventory owns on-hand stock, reserved stock, availability, order reservation state, reservation expiration, and stock commitment/release. Product owns catalog information; Order owns checkout/order state. Inventory never reads Product or Order databases.

```text
src/Services/Inventory/
├── Inventory.Api/
│   ├── Domain/
│   ├── Features/
│   ├── Integration/
│   ├── Persistence/
│   └── Program.cs
└── Inventory.Migrator/
```

Inventory owns its PostgreSQL schema and migrations. `Inventory.Migrator` applies migrations before API rollout.

## Stock model

`InventoryItem` is keyed by authoritative ProductId and tracks `OnHand`, `Reserved`, derived `Available`, timestamps, and optimistic `Version`. Domain operations prevent negative quantities, over-reservation, committing more than reserved/on-hand stock, and administrative stock reduction below active reservations. Failed operations do not partially mutate state.

The stock-management HTTP capability uses a strong ETag when modifying an existing item so concurrent administrative writes do not silently overwrite one another.

## Reservation workflow

`ReserveInventory` is a directed command owned by Inventory. One durable `InventoryReservation` exists per Order and contains the reserved product/quantity lines plus an expiration deadline. Duplicate command delivery converges on the same logical reservation/outcome.

Inventory publishes facts:

- `InventoryReserved`;
- `InventoryRejected`;
- `InventoryReservationCommitted`;
- `InventoryReservationExpired`.

Order may issue `ReleaseInventory` or `CommitInventoryReservation`. Command receive endpoint names are explicit stable infrastructure contracts matching the shared command constants.

Reservation and stock changes commit in Inventory's local transaction together with outgoing events through the PostgreSQL consumer/bus outbox. RabbitMQ delivery is at-least-once in the practical model; domain state and database constraints are the idempotency/concurrency fences.

`InventoryReservationExpirationWorker` releases stock for expired active reservations and publishes the expiration fact durably. Correctness does not depend on an in-memory timer.

## Security

Inventory's management endpoint is an authenticated resource API capability and uses its own least-privilege role. Order workflow commands arrive through the internal messaging boundary; they are not exposed as public HTTP orchestration endpoints.

## Context routing

- HTTP stock management -> `../architecture/vertical-slice.md` / `api-and-errors.md`;
- stock/reservation invariants -> `../architecture/domain-boundary.md`;
- schema/concurrency/migrations -> `../architecture/persistence.md`;
- reservation commands/events/outbox -> `../architecture/messaging.md`;
- authorization -> `../architecture/security.md`;
- verification -> `../testing-map.md`.
