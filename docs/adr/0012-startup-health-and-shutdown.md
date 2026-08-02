# ADR 0012: Startup, readiness, liveness, and shutdown

- Status: Accepted
- Date: 2026-08-02

## Context

Treating dependency outages as liveness failures creates restart loops. Ignoring broker or database
state in readiness sends work to an instance that cannot process it. Inconsistent stop timeouts can
interrupt consumers without a defined redelivery outcome.

## Decision

The host waits for MassTransit startup within `StartTimeout`; unavailable RabbitMQ or missing delayed
exchange support fails startup within that bound.

Liveness is process-focused. Readiness includes required service dependencies, MassTransit bus
health, and the outbox collector's latest query result. Backlog quantity is an alert, not a readiness
failure.

`ConsumerStopTimeout` must not exceed `StopTimeout`. Deployment termination grace must exceed stop
time plus load-balancer drain margin. Consumers propagate cancellation. In-flight work either
finishes during drain or remains unacknowledged and is safely redelivered.

## Consequences

Temporary external outages remove the instance from readiness without causing liveness restart
storms. Tests cover startup failure bounds, collector recovery, and active-consumer drain.
