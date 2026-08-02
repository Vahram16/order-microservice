# ADR 0006: Queue capacity, retention, TTL, dead-lettering, and parking

- Status: Accepted
- Date: 2026-08-02

## Context

RabbitMQ can discard a message that expires from a receive queue before MassTransit can route it to
`_error` or `_skipped`. Queue TTL therefore creates silent business-message loss without a complete
expiration topology.

## Decision

Durable business receive queues do not use `x-message-ttl`. The retired configuration key fails
startup validation.

Business queues use quorum durability, bounded count and bytes, `reject-publish` overflow, backlog
age, alerts, and operator intervention. Error and skipped queues retain independent bounded TTL and
length. Delayed-redelivery scheduling is separate from queue retention.

No general parking queue is created because business-message expiration is not an implemented
requirement. Adding expiration requires a new ADR, DLX, deterministic parking routing, durable queue,
retention, metrics, startup validation, ownership, and replay tests.

## Consequences

Existing queues with TTL require a controlled topology migration because RabbitMQ rejects
inequivalent redeclaration. Capacity rejection becomes visible to publishers instead of silently
removing old messages.
