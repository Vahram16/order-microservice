# Order Bounded Context

Use this document for work owned by `src/Services/Order`.

## Ownership

Order owns the commercial Order aggregate, immutable order-item and shipping snapshots, checkout idempotency, order status, deadlines, and durable coordination of inventory and payment outcomes. It does not own Product catalog state, stock, Stripe/provider state, reusable payment methods, or Customer profile data.

```text
src/Services/Order/
├── Order.Api/
│   ├── Domain/
│   ├── Features/
│   ├── Infrastructure/
│   ├── Integration/
│   ├── Persistence/
│   └── Program.cs
└── Order.Migrator/
```

`OrderDbContext` and Order migrations are service-owned. `Order.Migrator` runs before Order API rollout; the API never applies migrations during startup.

## Create-order boundary

`POST /api/v1/orders` accepts customer intent only: product identifiers/quantities, a Payment-owned payment-method identifier, and shipping data. The caller never supplies trusted `CustomerId`, unit prices, discounts, totals, or provider identifiers.

Order resolves Customer identity from its Customer-owned integration projection and takes trusted Product snapshots from `ProductCatalogChanged`. It persists the Order, immutable `OrderItem` snapshots, `OrderSubmission` idempotency fence, and the first `ReserveInventory` command through the PostgreSQL bus outbox in one local persistence boundary.

The current pricing policy is the latest Product catalog state successfully synchronized into Order. Historical Order items retain their accepted SKU/name/unit-price/currency snapshots. A future quote/pricing capability may replace this policy only through an explicit business requirement.

## Distributed workflow

Order is the checkout orchestrator, not a distributed transaction coordinator. Each service commits only its own database. Cross-service consistency uses commands/events, inbox/outbox delivery, idempotent state transitions, deadlines, and compensation.

Happy-path intent:

```text
Order created
  -> ReserveInventory
  <- InventoryReserved
  -> AuthorizeOrderPayment
  <- PaymentAuthorized (or PaymentActionRequired -> later PaymentAuthorized)
  -> CommitInventoryReservation
  <- InventoryReservationCommitted
  -> OrderConfirmed
```

Failure paths release inventory and/or cancel an outstanding payment authorization as appropriate. Late messages are treated as normal distributed-system cases and must either be idempotent or trigger an explicit compensation; message order is never assumed to be globally reliable.

3-D Secure is a first-class state. `PaymentActionRequired` moves Order into an awaiting-customer-action state. Order never stores Stripe client secrets or Stripe identifiers and never trusts a browser callback as payment authority. Payment resumes the workflow only after provider reconciliation.

Order deadlines are persisted business state. `OrderExpirationWorker` finds due non-terminal orders and performs the same durable compensation semantics as message-driven failure; no correctness depends on an in-memory timer surviving a process restart.

## Domain and persistence rules

The `Order` aggregate owns legal lifecycle transitions and failure atomicity. Expected failures return semantic `Result` values. `OrderItem` and shipping data are historical snapshots owned by the aggregate. Aggregate `Version` is an optimistic-concurrency token.

`OrderSubmission` closes duplicate HTTP retries with a customer-scoped idempotency key plus canonical request fingerprint. Same key/same request returns the original logical order; same key/different request is a conflict. Database uniqueness is the concurrency fence.

Order maintains small service-local projections required for request independence:

- `OrderCustomer`: authoritative CustomerId + authenticated identity correlation;
- `OrderProduct`: latest accepted Product catalog snapshot/version.

These projections are not new sources of truth for the upstream bounded contexts.

## Security

The resource API validates bearer tokens itself even when deployed behind an API Gateway. Endpoints use least-privilege Order roles and derive external identity only from validated token claims. Resource ownership is enforced by resolving that identity to the authoritative CustomerId projection; route/body values never establish ownership.

## Context routing

- API slice -> `../architecture/vertical-slice.md` and `api-and-errors.md`;
- aggregate/state transitions -> `../architecture/domain-boundary.md`;
- idempotency/concurrency -> `../architecture/concurrency-idempotency.md`;
- schema/migrations -> `../architecture/persistence.md`;
- commands/events/outbox -> `../architecture/messaging.md`;
- token/roles/ownership -> `../architecture/security.md`;
- verification -> `../testing-map.md`.
