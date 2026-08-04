# ADR 0008: Endpoint naming and topology migration

- Status: Accepted
- Date: 2026-08-02

## Context

Queue names and arguments are durable broker topology, not implementation details. Deriving an
endpoint name only from a CLR consumer type can accidentally change topology during a refactor.

## Decision

Business consumers use an explicit stable lowercase kebab-case endpoint name through standard
MassTransit registration. A service that needs consumer-specific concurrency, retry, or middleware
uses `ConsumerDefinition<TConsumer>` rather than a shared custom registry.

Global messaging defaults remain suitable for ordinary consumers. Endpoint-name configuration
overrides are optional operational tuning, not a mandatory policy framework.

An endpoint rename is a topology migration. Deployment plans must define old queue draining,
consumer and producer ordering, temporary old/new coexistence, rollback, and obsolete topology
removal.

## Consequences

Renaming a consumer class does not rename its broker endpoint. Queue type or argument changes may
require controlled queue replacement because RabbitMQ rejects inequivalent redeclaration.
