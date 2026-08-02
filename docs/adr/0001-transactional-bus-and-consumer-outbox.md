# ADR 0001: Transactional bus and consumer outbox

- Status: Accepted
- Date: 2026-08-02

## Context

Publishing directly to RabbitMQ cannot be atomic with an EF Core transaction. Consumer database
changes, produced messages, and acknowledgement also form a failure window.

## Decision

Services use MassTransit 8.5.10 Entity Framework bus outbox and consumer inbox/outbox with the same
configured scoped `TDbContext`.

Application publication is atomic with database state only when it occurs in the same service scope,
uses `IIntegrationMessagePublisher`, and commits the configured DbContext transaction. Consumer
produced messages remain buffered until consumer database work succeeds. Inbox state suppresses
transport duplicates within the configured duplicate-detection window.

External side effects and another DbContext/database are outside this atomic boundary and require
idempotency plus reconciliation.

## Consequences

Outbox schema migrations run before API rollout. Pending rows survive broker or process interruption
and are delivered after recovery. Cleanup state is not reported as pending work. Tests prove commit,
rollback, duplicate suppression, interruption recovery, and consumer drain behavior.
