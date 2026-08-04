# ADR 0001: Transactional bus and consumer outbox

- Status: Accepted
- Date: 2026-08-02

## Context

Publishing directly to RabbitMQ cannot be atomic with an EF Core transaction. Consumer database
changes, produced messages, and acknowledgement also form a failure window.

## Decision

Services use MassTransit 8.5.10 Entity Framework bus outbox and consumer inbox/outbox with the same
configured scoped `TDbContext`.

Event publication and command sending are atomic with database state only when they occur in the same
service scope, use `IIntegrationEventPublisher` or `IIntegrationCommandSender<TCommand>`, and commit
the configured DbContext transaction. Consumer-produced messages remain buffered until consumer
database work succeeds. Inbox state suppresses transport duplicates within the configured
duplicate-detection window.

External side effects and another DbContext/database are outside this atomic boundary and require
idempotency plus reconciliation.

## Consequences

Outbox schema migrations run before API rollout. Pending rows survive broker or process interruption
and are delivered after recovery. Cleanup state is not reported as pending work. Tests prove commit,
rollback, duplicate suppression, interruption recovery, event fan-out, command point-to-point
routing, and consumer drain behavior.
