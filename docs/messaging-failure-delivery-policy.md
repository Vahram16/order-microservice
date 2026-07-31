# Messaging failure-delivery policy

This document is the production contract for services registered through
`AddRabbitMqWithPostgresOutbox<TDbContext>`. The helper deliberately owns the common receive
pipeline so automatically configured consumers cannot bypass retry, redelivery, topology,
idempotency, lifecycle, and telemetry requirements.

## Delivery sequence

Every automatically configured consumer endpoint uses the following bounded sequence:

1. The consumer receives one initial delivery.
2. A failure classified as transient receives the configured short `UseMessageRetry` intervals.
   These attempts remain in memory and hold the delivery slot, so the intervals must remain short.
3. After immediate retries are exhausted, a transient failure receives the configured
   `UseDelayedRedelivery` intervals. Each broker redelivery receives the complete short retry
   sequence again.
4. When all retry and redelivery intervals are exhausted, MassTransit moves the message to the
   endpoint's `_error` queue and publishes its normal fault event.
5. A permanent or unclassified failure skips retry and redelivery and follows the `_error` path
   after the first attempt.

The arrays are bounded to ten positive intervals. Retry intervals may not exceed 30 seconds and
redelivery intervals may not exceed one day. There is no application requeue loop and application
code must not catch a failed message and republish it to its own input queue.

Publishing is not manually retried. Publishing performed by a consumer is protected by the
consumer outbox. Publishing performed through the scoped `IPublishEndpoint` or
`ISendEndpointProvider` in an application transaction is protected by the PostgreSQL bus outbox.

## Delayed-redelivery decision

The selected scheduler is RabbitMQ's `rabbitmq_delayed_message_exchange` plugin, configured through
`UseDelayedMessageScheduler`. The repository provides
`infrastructure/rabbitmq/Containerfile`, which pins:

- RabbitMQ 4.2.9 by image digest;
- delayed-message-exchange 4.2.0 by release version and SHA-256;
- the management and Prometheus plugins.

The plugin keeps delayed messages broker-backed across application restarts without introducing an
application scheduler database. Production clusters must install the same compatible plugin before
consumers are deployed. A missing or incompatible plugin is a deployment failure, not a reason to
fall back to immediate requeue.

Broker clustering, node placement, disk sizing, backup, disaster recovery, certificate issuance,
user permissions, and plugin upgrade validation remain platform responsibilities outside the
application.

## Exception classification

`IConsumerExceptionClassifier` is default-deny: an exception is retried only when a rule explicitly
classifies it as transient.

Shared transient categories are:

- `TimeoutException`;
- provider database exceptions whose public `IsTransient` property is `true`;
- `HttpRequestException` without an HTTP status or with 408, 429, 500, 502, 503, or 504;
- socket and I/O failures;
- exceptions implementing `ITransientConsumerFailure`.

Shared permanent categories include JSON/serialization failures, authorization/security failures,
argument and request-validation failures, unsupported operations, exceptions implementing
`IPermanentConsumerFailure`, and every unknown exception. A type implementing both marker interfaces
is permanent.

A service can register one or more `IConsumerExceptionRule` implementations. Rules run in
registration order before shared classification and may classify provider- or domain-specific
exceptions. A rule must inspect typed provider data or stable error codes. It must not mark every
exception from a dependency as transient.

Expected domain rejection must not be represented as a generic transient exception. Validation,
authorization, malformed payloads, unsupported contract versions, and permanent domain errors are
single-attempt failures.

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
    "OutboxQueryDelay": "00:00:01",
    "DuplicateDetectionWindow": "00:30:00",
    "OutboxMetricsInterval": "00:00:10",
    "UseQuorumQueues": true,
    "QueueMessageTimeToLive": "7.00:00:00",
    "QueueMaxLength": 100000,
    "QueueMaxLengthBytes": 1073741824,
    "QueueDeliveryLimit": 10,
    "FaultQueueRetention": "14.00:00:00",
    "FaultQueueMaxLength": 10000,
    "MaximumMessageBytes": 1048576,
    "Consumers": {
      "service-template-submit-order": {
        "RetryIntervals": [ "00:00:00.500", "00:00:02" ],
        "RedeliveryIntervals": [ "00:00:30", "00:02:00" ],
        "PrefetchCount": 8,
        "ConcurrentMessageLimit": 2,
        "RateLimit": 20,
        "RateLimitInterval": "00:00:01",
        "SingleActiveConsumer": false
      },
      "service-template-ordered-command": {
        "PrefetchCount": 1,
        "ConcurrentMessageLimit": 1,
        "SingleActiveConsumer": true
      }
    }
  }
}
```

The `Consumers` key is the final stable endpoint/queue name. A rename is a topology migration. A
per-consumer policy inherits omitted values from the global policy. Rate limit and rate interval must
be configured together. `SingleActiveConsumer` requires prefetch and concurrency to both equal one.

### Backpressure and ordering

Prefetch and concurrency are intentionally independent. Prefetch limits broker deliveries buffered
by the process; concurrency limits active consumer work. Sensitive downstream dependencies should
receive a dedicated endpoint override rather than forcing one global bottleneck.

Use the built-in rate limiter for a dependency with a documented throughput ceiling. Use the endpoint
configuration callback for key-based partitioning when independent keys may run concurrently but one
key must remain serialized.

RabbitMQ enqueue order does not guarantee completion order when concurrency, retry, or redelivery is
present. An ordering-sensitive consumer must explicitly use one of these contracts:

- prefetch one, concurrency one, and `SingleActiveConsumer=true`; or
- a documented partition key and partitioner, with ordering guaranteed only inside one partition.

## Queue topology

Automatically configured receive, `_error`, and `_skipped` queues are durable and non-auto-delete.
Quorum queues are enabled by default. Receive queues declare:

- message TTL;
- maximum ready-message count;
- maximum ready-message bytes;
- `reject-publish` overflow behavior;
- a broker delivery limit when quorum queues are enabled.

Fault queues declare their own retention and maximum count. Their quorum delivery limit is unlimited
because no consumer automatically requeues them; replay is an explicit operator action.

`MaximumMessageBytes` is validated against queue capacity. The supplied RabbitMQ image enforces the
same one-megabyte broker limit in `rabbitmq.conf`. Every deployment that overrides either value must
keep the application and broker values aligned.

Queue names, exchange names, bindings, queue type, arguments, and message type identity are durable
infrastructure. Renaming a queue, changing an incompatible queue argument, moving a contract type, or
changing an exchange convention requires a reviewed migration with dual-read/dual-publish or drain
steps. It is not a routine application configuration change.

Cluster replication factor, availability-zone placement, leader balancing, disk alarms, memory
watermarks, federation, shovel policy, and backup/restore are defined by the broker platform, not by
application startup.

## Poison-message ownership and replay

The service that owns a receive endpoint owns its `_error` and `_skipped` queues.

### Retention and alerting

- `_error` and `_skipped` queues default to 14 days and 10,000 messages.
- Any `_error` message is critical and must create an incident routed to the service owner.
- Any `_skipped` message is a separate critical signal for routing or contract mismatch.
- Oldest fault-message age above fifteen minutes indicates missing incident ownership.
- Retention must be shortened when payload classification or regulation requires it; it must never
  be extended casually as a substitute for incident response.

### Redaction

Application logs, traces, metrics, alerts, and dashboard labels must not contain message bodies,
credentials, access tokens, payment data, secrets, or unrestricted personal data. Use MessageId,
CorrelationId, endpoint, contract type, exception type, and approved domain identifiers. Payload
inspection is restricted to access-controlled broker or incident tooling with an audit trail.

### Safe replay

Replay requires all of the following:

1. an incident or change ticket with a named owner;
2. a confirmed root cause and deployed fix;
3. a consumer version compatible with the stored contract;
4. preservation of MessageId, CorrelationId, CausationId, contract version, and W3C trace context;
5. validation of external-side-effect idempotency keys;
6. a bounded batch size and rate;
7. observation of success, duplicate suppression, and new `_error`/`_skipped` traffic;
8. an explicit stop condition and rollback procedure.

Never configure an automatic shovel from `_error` or `_skipped` to the source queue. Never replay by
creating new identifiers merely to bypass duplicate detection.

## Idempotency and identifiers

Application-owned integration contracts implement `IIntegrationMessage`:

```csharp
public interface IIntegrationMessage
{
    Guid MessageId { get; }
    Guid CorrelationId { get; }
    Guid? CausationId { get; }
    int ContractVersion { get; }
}
```

The send, publish, and consume filters reject empty identifiers, non-positive versions, and mismatches
between payload identifiers and transport headers. `MessageId` identifies one logical message and is
stable across safe replay. `CorrelationId` identifies the business operation. `CausationId`
identifies the parent message where one exists. OpenTelemetry preserves the independent W3C trace
relationship.

The PostgreSQL consumer inbox suppresses duplicate delivery for the configured duplicate-detection
window. The consumer outbox makes database changes and produced messages atomic only when they use
the same scoped `TDbContext` configured in
`AddRabbitMqWithPostgresOutbox<TDbContext>`.

The following are outside that database transaction and require their own durable idempotency key,
provider contract, and reconciliation process:

- another `DbContext` or database;
- HTTP or gRPC APIs;
- email, SMS, and push notifications;
- payment authorization, capture, refund, and payout operations;
- object storage, file systems, and document generation;
- search indexes and third-party SaaS operations.

An external idempotency key should normally derive from the stable MessageId plus an operation name,
not from a retry attempt number.

## Lifecycle and dependency recovery

MassTransit waits for bus startup and applies explicit start, stop, and consumer-stop timeouts.
During host shutdown the existing service lifecycle readiness check becomes unhealthy before the
host drains in-flight consumers. `ConsumerStopTimeout` must not exceed `StopTimeout`.

MassTransit's RabbitMQ client recovery handles broker disconnect/reconnect. Npgsql and EF execution
strategies handle eligible transient database failures. Neither mechanism changes exception
classification or creates an unbounded message loop.

Readiness includes the MassTransit bus and the service database. A disconnected required dependency
must make the instance unready so traffic and new work are removed while recovery occurs. Liveness
must remain process-focused so an external dependency outage does not cause a restart storm.

CI force-closes RabbitMQ client connections and terminates a PostgreSQL backend to verify recovery.
It also verifies that shutdown waits for an active consumer to complete within the configured drain
window.

## Observability

The application exports the existing MassTransit OpenTelemetry meter plus the
`Microservices.Messaging` meter. The shared meter includes:

- `messaging.consumer.retry.attempts`;
- `messaging.consumer.redelivery.attempts`;
- `messaging.consumer.failures`;
- `messaging.consumer.attempt.duration`;
- `messaging.outbox.backlog`;
- `messaging.outbox.oldest.age`;
- `messaging.outbox.collection.failures`.

The custom RabbitMQ image exposes the Prometheus plugin on port 15692. Use the restricted scrape
configuration in `infrastructure/observability/prometheus/rabbitmq-scrape.yml`; it collects aggregate
broker metrics and only the queue metric families needed for depth and head-message timestamp.
Scraping every per-object metric family on a large cluster is prohibited without a cardinality
review.

Deploy:

- `infrastructure/observability/prometheus/messaging-alerts.yml`;
- `infrastructure/observability/grafana/messaging-reliability-dashboard.json`.

The dashboard and alerts cover retry and redelivery counts, consumer failures and p95 latency,
`_error` and `_skipped` depth, oldest queued-message age, outbox backlog and age, metrics-collection
failure, broker scrape failure, and connection close/open churn. Environment owners must route
`owner=service-owner` alerts by endpoint ownership and `owner=platform` alerts to the broker team.
Thresholds are checked-in starting values and must be capacity-tested before production rollout.

## Contract governance

Integration contract evolution follows these rules:

1. Prefer additive optional fields with safe defaults.
2. Do not rename or remove serialized fields from a deployed version.
3. Do not change a field's meaning, type, units, timezone, or identifier semantics in place.
4. Introduce a new contract type/version for an incompatible change and run a migration period.
5. Preserve MessageId, CorrelationId, CausationId, and trace context across adapters and versions.
6. Keep `ContractVersion` stable for additive changes to the same contract; increment it only for a
   deliberately introduced version that the consumer explicitly supports.
7. Keep payloads below `MaximumMessageBytes`; large documents belong in controlled storage with an
   integrity-checked reference.
8. Do not publish credentials, tokens, raw payment data, secrets, or unnecessary personal data.
9. Classify every field and document retention before introducing sensitive data.
10. Treat queue renames, message namespace/type moves, and incompatible serializer changes as
    migrations with rollback plans.

## Behavioral verification

`MessagingFailureDeliveryBehaviorTests` runs against real RabbitMQ and PostgreSQL resources in CI.
It verifies:

- a transient failure succeeds after configured immediate retries;
- a permanent failure receives one attempt;
- retry/redelivery exhaustion reaches `_error`;
- delayed redelivery observes the configured increasing intervals;
- duplicate delivery does not repeat database changes;
- transaction rollback publishes no messages;
- a successful commit publishes exactly once through the bus outbox;
- RabbitMQ connection recovery after forced disconnect;
- PostgreSQL recovery after backend termination;
- graceful shutdown drains an active consumer.

The in-memory MassTransit harness is not a substitute for these tests because it cannot prove broker
queue topology, the delayed-exchange plugin, PostgreSQL inbox/outbox atomicity, or transport recovery.
