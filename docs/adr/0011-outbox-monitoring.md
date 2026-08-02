# ADR 0011: Outbox monitoring

- Status: Accepted
- Date: 2026-08-02

## Context

Repeated table-wide counts can become expensive, cleanup state can be mistaken for pending work, and
a database failure must not appear as zero backlog.

## Decision

The collector reports pending `OutboxMessage` rows separately for bus and consumer roles using the
MassTransit 8.5.10 schema predicates. Delivered `OutboxState` and completed `InboxState` records are
not backlog.

Two aggregate queries use role-specific partial indexes ordered by `SentTime`. The migration builds
indexes concurrently. Collection has a query timeout, never overlaps, rate-limits logs, retains last-
known values on failure, exposes independent health/staleness/failure metrics, and recovers
automatically.

## Consequences

Zero backlog is emitted only after a successful query. A failed concurrent index build may leave an
invalid index and requires operator repair before migration retry. Query plans and large-backlog
behavior are verified against PostgreSQL in the reliability suite.
