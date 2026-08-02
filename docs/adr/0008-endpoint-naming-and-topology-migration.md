# ADR 0008: Endpoint naming and topology migration

- Status: Accepted
- Date: 2026-08-02

## Context

Policies selected by formatted CLR names can silently disappear after a rename, formatter change, or
configuration typo. Queue names and arguments are durable broker topology, not implementation detail.

## Decision

Business consumers register through `AddConsumerWithPolicy<TConsumer>` with an explicit stable
lowercase kebab-case endpoint name. Startup validates policy matches, collisions, critical
concurrency, ordering, rate limits, and names. Validated global defaults are disabled unless a
service explicitly enables them.

An endpoint rename is a topology migration. Deployment plans must define old queue draining,
consumer and producer ordering, temporary old/new coexistence, rollback, and obsolete topology
removal.

## Consequences

Renaming a consumer class does not rename its broker endpoint. A stale policy fails startup instead
of falling back. Queue type or argument changes may require queue replacement because RabbitMQ does
not accept inequivalent redeclaration.
