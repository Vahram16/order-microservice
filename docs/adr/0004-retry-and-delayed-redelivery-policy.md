# ADR 0004: Bounded retry, delayed redelivery, and failure classification

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

Immediate retries hold a consumer slot, while long or unbounded retry schedules can overload a
failing dependency. Retry safety depends on the operation and its idempotency, not merely on broad
HTTP, socket, timeout, or database exception categories. Multiple retry layers can also multiply the
number of attempts unexpectedly.

## Decision

The shared receive-endpoint policy uses a small bounded immediate retry sequence followed by bounded
RabbitMQ delayed redelivery. Longer waits are broker-backed and release the consumer slot.

Only failures explicitly classified as transient enter this path. The shared classifier understands
transient, permanent, outcome-unknown, and cancellation markers plus narrow service-owned
`IConsumerExceptionRule` implementations. Unknown failures are permanent by default. Permanent or
cancelled evidence in wrapped or aggregate failures takes precedence over transient evidence.

A service-owned rule must use stable provider information and may classify a failure as transient
only when replay is safe for that operation. The shared endpoint policy remains the single retry and
redelivery middleware stack; service-specific tuning uses the supported endpoint overrides. A
`ConsumerDefinition<TConsumer>` may configure other consumer behavior but must not add a second retry
stack.

The deployed RabbitMQ image and deployment smoke tests own delayed-exchange capability verification.
Applications do not open a second raw RabbitMQ connection solely to probe the plugin.

## Consequences

Failure handling is bounded and default-deny. Service owners must review message retry together with
HTTP, database, and client-library resilience so the combined attempt count remains intentional.
Integration tests verify recovery, bounded attempts, delayed redelivery, and terminal `_error`
placement.
