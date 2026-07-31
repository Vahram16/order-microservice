# Messaging failure-delivery policy

## Delivery sequence

Every automatically configured consumer endpoint uses this bounded sequence:

1. The consumer executes once.
2. Transient failures receive the configured short `UseMessageRetry` intervals.
3. After immediate retries are exhausted, transient failures receive the configured
   `UseDelayedRedelivery` intervals.
4. After all configured redeliveries are exhausted, MassTransit moves the message to the endpoint's
   `_error` queue and publishes the normal fault event.
5. Permanent failures skip retry and redelivery and move to `_error` after the first attempt.

There is no infinite retry or requeue path. Application code must not catch a failed message and
republish it to its own input queue. Publishing performed by a consumer is already protected by the
consumer outbox, and publishing performed in an application transaction is protected by the bus
outbox.

## Scheduler decision

The transport uses RabbitMQ's delayed-message exchange plugin through
`UseDelayedMessageScheduler`. Production RabbitMQ clusters must install and enable the
`rabbitmq_delayed_message_exchange` plugin before deploying consumers. This avoids an additional
application scheduler database while keeping delayed messages broker-backed across service process
restarts. Plugin installation, clustering, mirrored/quorum queue policy, and disaster recovery are
broker-platform responsibilities outside the application.

## Exception classification

The shared classifier retries only known transient categories:

- timeouts;
- temporary database/provider exceptions represented by `DbException`;
- temporary downstream HTTP failures represented by `HttpRequestException`;
- temporary I/O and socket failures;
- exceptions implementing `ITransientMessageException`.

Serialization, authorization/security, argument/validation, unsupported-operation, and exceptions
implementing `IPermanentMessageException` are permanent. Unknown exceptions are permanent by
default; this prevents accidental retry storms.

A service can register one or more `IConsumerExceptionRule` implementations. Rules run before the
shared defaults and may classify service/provider-specific exceptions as transient or permanent.
Rules must inspect typed exception data, error codes, and documented provider semantics; they must
not classify all exceptions from a dependency as transient.

## Configuration

```json
{
  "Messaging": {
    "RetryIntervals": [ "00:00:00.200", "00:00:01", "00:00:03" ],
    "RedeliveryIntervals": [ "00:00:15", "00:01:00", "00:05:00" ],
    "PrefetchCount": 32,
    "ConcurrentMessageLimit": 8,
    "StartTimeout": "00:00:30",
    "StopTimeout": "00:00:30",
    "ConsumerStopTimeout": "00:00:25",
    "Consumers": {
      "service-template-submit-order": {
        "RetryIntervals": [ "00:00:00.500", "00:00:02" ],
        "RedeliveryIntervals": [ "00:00:30", "00:02:00" ],
        "PrefetchCount": 8,
        "ConcurrentMessageLimit": 2
      }
    }
  }
}
```

The `Consumers` key is the final stable endpoint/queue name. Renaming it is a topology migration.
Use the endpoint callback for dependency-specific rate limiting, partitioning, or explicit ordering.
Ordering-sensitive consumers must use a concurrency limit of one or a documented partition key;
RabbitMQ queue order alone does not guarantee completion order with concurrent consumers, retries,
or redelivery.

## Poison messages

- Exhausted and permanent failures are owned by the service that owns the receive endpoint.
- `_error` queues must be durable, access-controlled, and retained according to incident and data
  retention policy. A recommended starting point is 14 days, overridden for regulated payloads.
- Alert on any new `_error` message and on queue depth/oldest age thresholds.
- `_skipped` queues are monitored separately because they indicate topology or contract routing
  mismatches, not consumer execution failures.
- Logs, alerts, and dashboards must redact credentials, tokens, personal data, payment data, and raw
  message bodies. Operators should use identifiers and approved secure tooling to inspect payloads.
- Replay requires an incident ticket, identified root cause, compatible deployed consumer, preserved
  MessageId/CorrelationId, and a bounded batch. Never replay by automatic shovel back to the source
  queue without a stop condition.

## Idempotency and identifiers

The PostgreSQL consumer inbox/outbox remains the database idempotency mechanism. Producers must set
stable `MessageId` values for logical messages and must preserve them when safely replaying. Set
`CorrelationId` to the business operation identifier. MassTransit propagates the consumed
correlation as `InitiatorId`; this is the causation convention for child messages. Preserve W3C trace
context through the existing OpenTelemetry instrumentation.

Database atomicity covers only work saved through the same `TDbContext` instance used by the
consumer outbox. A second DbContext, another database, external API, email, payment, object-storage,
or file operation is outside that transaction and requires its own idempotency key and reconciliation
strategy.

## RabbitMQ topology and operations

Production queues must be durable and should use quorum queues unless workload testing demonstrates
a documented exception. Apply queue TTL, maximum length/bytes, and broker message-size policies at
the broker/operator layer. Contract changes are additive by default; queue renames, removed fields,
renamed message types, and incompatible payload changes require a migration plan.

Dashboards and alerts must cover retry/redelivery counts, consumer failures and latency, `_error` and
`_skipped` depth, oldest message age, outbox backlog and delivery age, and broker disconnect/reconnect
signals. Readiness must become unhealthy while the bus or required database is unavailable. Shutdown
must remove readiness first and allow in-flight consumers to drain within the configured consumer and
host stop timeouts.

## Required behavioral verification

The integration suite must exercise RabbitMQ and PostgreSQL and cover transient retry success,
first-attempt permanent failure, `_error` routing after exhaustion, exact redelivery intervals,
duplicate delivery, rollback/no-publish, commit/exactly-once outbox delivery, broker/database outage
recovery, and graceful draining. These are environment-backed behavioral tests and should run in CI
with isolated broker/database resources rather than using only the in-memory harness.
