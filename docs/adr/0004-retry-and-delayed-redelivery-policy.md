# ADR 0004: Bounded retry and delayed redelivery

- Status: Accepted
- Date: 2026-08-02

## Context

Immediate retries hold a consumer slot. Delayed redelivery is a new broker delivery and must not be
reported as another immediate retry. Unbounded or multiplied retries can overload dependencies and
extend failure handling unpredictably.

## Decision

RabbitMQ delayed-message exchange redelivery wraps bounded immediate MassTransit retry. The consumer
attempt telemetry filter is inside both middleware components. The delayed plugin is required; there
is no fallback to immediate requeue.

Configuration validation bounds interval count, interval duration, and total middleware delay. A
transient failure receives the complete immediate sequence for every delayed delivery. Permanent,
cancelled, outcome-unknown, and unclassified failures do not enter the shared retry path.

## Consequences

Tests assert exact invocation, retry, redelivery, and final `_error` placement counts. Dependency
resilience policies must be reviewed together with messaging policy to prevent retry multiplication.
