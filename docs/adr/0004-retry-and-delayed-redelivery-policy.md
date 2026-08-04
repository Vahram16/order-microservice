# ADR 0004: Bounded retry and delayed redelivery

- Status: Accepted
- Date: 2026-08-02

## Context

Immediate retries hold a consumer slot. Unbounded retry or retry multiplication can overload a
failing dependency. Longer waits should release the consumer slot and be broker-backed.

## Decision

Receive endpoints use a small bounded immediate retry sequence followed by bounded RabbitMQ delayed
redelivery. Only failures explicitly classified as transient enter this path. Unknown, permanent,
outcome-unknown, and cancelled failures are not retried by the shared policy.

The deployed RabbitMQ image and deployment smoke tests verify delayed-exchange availability. The
application does not open a second raw RabbitMQ connection solely to probe the plugin.

Services with materially different requirements configure the consumer through MassTransit
`ConsumerDefinition<TConsumer>` or a narrow endpoint override.

## Consequences

Integration tests verify observable behavior: successful recovery, bounded attempts, and final
`_error` placement. Service owners must review HTTP/database/client resilience together with message
retry to avoid multiplied attempts.
