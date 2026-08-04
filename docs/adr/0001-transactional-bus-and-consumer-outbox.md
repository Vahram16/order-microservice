# ADR 0001: Transactional messaging with bus and consumer outboxes

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

A database transaction and a RabbitMQ publish cannot be committed atomically. Consumers have the
same failure window between database changes, produced messages, and broker acknowledgement.

## Decision

Services use the MassTransit Entity Framework bus outbox and consumer inbox/outbox with the same
scoped `TDbContext` that owns the business transaction.

Application code publishes events through `IIntegrationEventPublisher` and sends commands through
`IIntegrationCommandSender<TCommand>`. A message is part of the database transaction only when the
operation uses the configured service scope and the owning DbContext work is saved and committed.
Consumer-produced messages remain buffered until consumer database work succeeds.

Transport duplicate suppression is provided within the configured duplicate-detection window.
External side effects and work performed through another DbContext or database remain outside this
atomic boundary and require idempotency and reconciliation.

## Consequences

Outbox schema migrations must run before application rollout. Committed pending rows survive broker
or process interruption and are delivered after recovery. Delivery remains at-least-once outside the
inbox window, so business handlers must protect non-transactional side effects appropriately.
