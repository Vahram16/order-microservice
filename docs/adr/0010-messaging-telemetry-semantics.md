# ADR 0010: Messaging telemetry semantics

- Status: Accepted
- Date: 2026-08-02

## Context

A filter outside retry middleware can observe only the final lifecycle result. Dashboards that mix
failed attempts with messages finally placed in `_error` create incorrect incidents and reliability
claims.

## Decision

MassTransit built-in OpenTelemetry remains primary. Custom telemetry is placed inside retry and
redelivery and records only:

- immediate retry invocations;
- broker-backed delayed deliveries;
- individual failed consumer invocations;
- individual invocation duration;
- outbox backlog, age, collector health, staleness, and failures.

RabbitMQ `_error` and `_skipped` queue depth are the final-placement signals. Metric labels are
bounded and exclude identifiers, payload data, URLs, and exception messages.

## Consequences

Dashboards and alerts separately display transient attempts, delayed deliveries, invocation
failures, terminal errors, and skipped messages. Tests assert exact increments for success, retries,
redelivery, exhaustion, permanent failure, and skipped routing.
