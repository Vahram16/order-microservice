# ADR 0006: Queue durability, capacity, and failure retention

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

A message that expires from a business receive queue can disappear before MassTransit routes it to
`_error` or `_skipped`. Unbounded queues can instead exhaust broker resources and turn an application
incident into a platform incident.

## Decision

Durable business receive queues do not use `x-message-ttl`. The retired receive-queue TTL setting
fails startup validation.

Business queues are durable, non-auto-delete quorum queues by default. They use bounded message count
and byte capacity with `reject-publish` overflow, plus a bounded broker delivery limit as a final
guard against requeue loops outside the MassTransit retry pipeline.

MassTransit `_error` and `_skipped` queues have independent bounded retention and capacity. Delayed
redelivery scheduling is separate from receive-queue retention.

No general parking queue is created because business-message expiration and replay are not currently
implemented requirements. Introducing them requires an explicit dead-letter topology, durable
parking destination, ownership, retention, observability, and replay tests.

## Consequences

Capacity pressure becomes visible to publishers instead of silently deleting old business work.
Changing queue type, name, or immutable arguments requires a controlled topology migration because
RabbitMQ rejects inequivalent redeclaration.
