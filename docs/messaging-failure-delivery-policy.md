# Messaging reliability contract

This document describes behavior enforced by production code registered through
`AddRabbitMqWithPostgresOutbox<TDbContext>`. It is not a roadmap. A behavior is described as a
guarantee only when code and automated tests enforce it.

## Verified dependency baseline

The repository centrally pins:

- MassTransit, MassTransit.RabbitMQ, and MassTransit.EntityFrameworkCore 8.5.10;
- Entity Framework Core 10.0.10;
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3;
- OpenTelemetry 1.17.0;
- xUnit 2.9.3 and Microsoft.NET.Test.Sdk 18.3.0;
- Testcontainers 4.13.0;
- RabbitMQ 4.2.9 and delayed-message-exchange plugin 4.2.0.

A package upgrade that can change middleware, topology, serializer, outbox, or health behavior must
rerun the complete reliability suite and update this document where semantics differ.

## Receive pipeline and delivery lifecycle

The endpoint pipeline is configured in this order:

1. delayed redelivery;
2. immediate message retry;
3. consumer-attempt telemetry;
4. consumed-parent context capture;
5. Entity Framework consumer outbox/inbox;
6. the consumer.

MassTransit filters wrap the next pipe. Consequently delayed redelivery owns the complete immediate
retry sequence, while the attempt filter is inside both middleware components and sees every actual
consumer invocation.

For a transient failure:

1. the initial broker delivery invokes the consumer;
2. each configured immediate retry invokes the consumer again in memory;
3. after the immediate sequence is exhausted, the message is scheduled for broker-backed delayed
   redelivery;
4. each delayed delivery receives a new complete immediate retry sequence;
5. after all configured redeliveries are exhausted, MassTransit transfers the message to the
   endpoint's `_error` queue.

A permanent, cancelled, outcome-unknown, or unclassified failure is not retried by the shared
classifier. Unknown failures are default-deny.

`MaximumRetryAndRedeliveryDelay` bounds configured middleware delay. The validator calculates:

```text
(redelivery count + 1) × sum(immediate retry intervals)
+ sum(redelivery intervals)
```

Consumer execution time, dependency timeouts, and shutdown time are separate bounds and are not
hidden inside this value.

## Telemetry semantics

MassTransit's OpenTelemetry instrumentation remains the primary source for receive, consume, fault,
and transport spans and measurements. The custom meter contains only signals needed to distinguish
attempts from complete delivery lifecycles.

| Metric | Exact meaning |
|---|---|
| `messaging.consumer.retry.attempts` | One immediate retry invocation. Retries occurring inside a delayed delivery are included. |
| `messaging.consumer.redelivery.deliveries` | One broker-backed delayed delivery, counted before that delivery's immediate retry sequence. |
| `messaging.consumer.attempt.failures` | One consumer invocation that threw, even when a later invocation succeeds. |
| `messaging.consumer.attempt.duration` | Duration of one consumer invocation. It does not include retry or redelivery waiting time. |
| `messaging.outbox.backlog` | Pending `OutboxMessage` rows, split into bounded `bus` and `consumer` roles. |
| `messaging.outbox.oldest.age` | Age of the oldest pending `OutboxMessage` for one role. |
| `messaging.outbox.collector.healthy` | `1` after the latest successful collection; `0` before first success or after collection failure. |
| `messaging.outbox.collector.last_success.age` | Seconds since the last successful collection. |
| `messaging.outbox.collection.failures` | Collector query failures. |

A failed invocation is not a terminally failed message. RabbitMQ `_error` queue depth is the
authoritative terminal-placement signal. `_skipped` queue depth is a separate routing or contract
signal. Dashboards and alerts intentionally keep these signals separate.

Application metric labels are bounded to service, stable endpoint name, contract type, exception
type, failure disposition, DbContext type, and outbox role. Message IDs, correlation IDs, exception
messages, URLs, customer IDs, order IDs, payload data, and arbitrary endpoint addresses are not
metric labels.

## Queue retention and capacity

Durable business receive queues do not declare `x-message-ttl`. The retired
`Messaging:QueueMessageTimeToLive` key causes startup validation failure.

Business queues use:

- durable, non-auto-delete topology;
- quorum queues by default;
- maximum ready-message count;
- maximum ready-message bytes;
- `reject-publish` overflow behavior;
- quorum delivery limit as a final guard against external requeue loops;
- backlog depth and oldest-message-age alerts.

RabbitMQ cannot silently discard an expired business message before MassTransit sees it because no
receive-queue TTL is configured. Per-message expiration or a future parking topology requires a new
reviewed ADR, deterministic routing test, metrics, ownership, retention, and replay procedure.

`_error` and `_skipped` queues have their own bounded retention and maximum length. Those values do
not apply to business receive queues or delayed-redelivery scheduling.

Removing an existing queue argument is a topology migration. RabbitMQ rejects redeclaration when an
existing queue has inequivalent arguments. Deployments must drain or replace queues as described in
the endpoint migration runbook; they must not repeatedly restart the application against an
incompatible queue.

## Approved publishing boundary

Production application code publishes through
`Microservices.Application.Messaging.IIntegrationMessagePublisher`.

The infrastructure implementation is scoped and uses MassTransit's scoped `IPublishEndpoint`, which
participates in the configured Entity Framework bus outbox when publication occurs in the same
service scope and the same configured `TDbContext` transaction.

The publisher:

- propagates the cancellation token;
- assigns a MessageId when one is not explicitly supplied;
- propagates the parent CorrelationId, or creates one from the new MessageId;
- propagates the consumed parent MessageId as InitiatorId and `x-causation-id`;
- accepts a bounded set of application headers;
- rejects attempts to override transport-owned identity headers.

Publication outside the configured database transaction is still durable transport publication, but
it is not atomic with arbitrary application state. Another DbContext, another database, HTTP, gRPC,
email, payment, file, object-storage, and SaaS side effects require dependency-specific idempotency
and reconciliation.

Architecture tests reject production dependencies on `IBus`, `IBusControl`, raw RabbitMQ clients,
transport-specific send endpoint providers, broker channels, or broker connections. Infrastructure
composition and explicitly named test fixtures are the only approved direct-bus locations.

## Consumer policy governance and endpoint identity

Use `AddConsumerWithPolicy<TConsumer>(stableEndpointName, policy)` for business consumers.

The endpoint name is a stable broker topology identifier, not a formatted CLR class name. Names must
be lowercase kebab case, 1–128 characters, with no leading, trailing, or repeated hyphens.

Startup fails when:

- a configured legacy policy matches no endpoint;
- a typed policy matches no endpoint;
- two typed consumers resolve to the same endpoint name;
- an endpoint has no policy and `AllowValidatedDefaultConsumerPolicy` is false;
- a critical policy omits explicit prefetch or concurrency;
- concurrency is zero or exceeds prefetch;
- a rate limit is incomplete;
- an ordering-sensitive endpoint is not prefetch one, concurrency one, and single-active-consumer;
- the configured retry/redelivery delay exceeds the documented maximum.

`AllowValidatedDefaultConsumerPolicy` defaults to false. A service may set it to true only as an
explicit architectural decision for endpoints that are safe under the validated global policy.

An endpoint rename is a broker migration. The old and new queue may need temporary coexistence,
controlled producer deployment ordering, old-queue draining, rollback capability, and explicit
obsolete-topology removal.

## Contract governance

`IIntegrationMessage` is the canonical marker. `IIntegrationEvent` represents a fact owned by the
publishing bounded context and includes `OccurredAtUtc`. `IIntegrationCommand` represents a request
with one logical owning bounded context.

Transport identity, correlation, causation, retry state, trace state, and broker headers are not
payload fields. Contracts remain independent from EF Core entities, DbContexts, aggregates,
controllers, consumers, and transport infrastructure.

Contract rules:

- the publishing bounded context owns an event contract;
- a receiver must reference that contract rather than redefining the same message;
- names use past tense for events and imperative intent for commands;
- identifiers use stable domain-neutral scalar types;
- timestamps are UTC `DateTimeOffset` values;
- optional additions must have safe defaults;
- absent collections deserialize to an explicitly handled empty state rather than relying on mutable
  shared instances;
- existing field names, types, units, nullability meaning, and semantics are not changed in place;
- additive compatible changes keep the same message identity;
- breaking changes use a distinct CLR namespace/type identity such as `.V2`, with a coexistence and
  migration period;
- a mutable integer payload version is not used as the sole breaking-version mechanism.

The transport serializer is configured explicitly for camel-case names, strict numeric handling,
case-sensitive properties, no comments or trailing commas, ignored unknown additive fields, and
omission of null fields. Historical-payload tests verify supported additive compatibility.

## Transient failure classification

The shared classifier is conservative.

Supported transient examples include:

- PostgreSQL connection class `08`, transaction rollback class `40`, connection-slot exhaustion
  `53300`, lock-not-available `55P03`, and administrative shutdown codes `57P01`–`57P03`;
- HTTP 408, 429, 502, 503, and 504;
- selected socket connection, network, timeout, and retryable availability errors;
- an explicitly registered dependency rule using stable provider data;
- an explicitly marked `ITransientConsumerFailure` whose operation is safe and idempotent.

Permanent examples include validation and argument errors, JSON or malformed data, unsupported
operations or contract versions, authentication and authorization failures, invalid certificates,
invalid endpoints or configuration, deterministic mapping failures, PostgreSQL integrity,
authentication, syntax, and schema classes, and explicit `IPermanentConsumerFailure`.

`TimeoutException`, arbitrary `IOException`, statusless `HttpRequestException`, and generic HTTP 500
are not shared transient categories. They require a dependency-specific rule because they can
represent invalid configuration, deterministic defects, cancellation, or an operation with unknown
remote outcome.

`OperationCanceledException` is classified as cancellation, not transient failure. An
`IOutcomeUnknownConsumerFailure` is permanent by default; retry is allowed only through a specific
rule that proves an idempotency key or reconciliation contract.

Permanent classification takes precedence when a wrapped or aggregate exception contains both
transient and permanent evidence.

## Outbox monitoring

For MassTransit 8.5.10, pending publish work is represented by `OutboxMessage` rows:

- `OutboxId IS NOT NULL` identifies bus-outbox messages;
- `OutboxId IS NULL` with both inbox identifiers populated identifies consumer-produced messages.

Delivered `OutboxState` retention rows and completed `InboxState` records are not counted as pending
messages.

The collector executes two indexable aggregate queries for exact count and oldest `SentTime`. The
ServiceTemplate migration adds partial indexes for the two role predicates. Index creation is
`CONCURRENTLY` and transaction suppression is intentional so a populated outbox table does not block
normal writes during deployment.

A failed concurrent index build can leave an invalid index. Before rerunning the migration, inspect
`pg_index.indisvalid`; drop the invalid index or use the approved concurrent reindex procedure.

Collector failures do not crash the service and do not overwrite last-known backlog values with
zero. The collector exposes independent health, staleness, failure metrics, rate-limited logs, query
timeout handling, cancellation, and automatic recovery. `PeriodicTimer` prevents overlapping
executions.

## Startup, readiness, liveness, and shutdown

The process waits for MassTransit startup up to `StartTimeout`. A required RabbitMQ connection or
required delayed-exchange capability that cannot be established causes startup failure within the
configured bound.

Liveness remains process-focused. Temporary RabbitMQ, PostgreSQL, or collector outages must not
cause a liveness restart loop.

Readiness includes required service dependencies, MassTransit bus state through its registered
health check, and the outbox collector's latest query result. Outbox backlog quantity is an alerting
and capacity signal, not a readiness failure by itself.

The default timeouts are:

- start: 30 seconds;
- host/MassTransit stop: 30 seconds;
- consumer stop: 25 seconds.

`ConsumerStopTimeout` must not exceed `StopTimeout`. Deployment termination grace must exceed the
application stop timeout plus load-balancer drain margin. In-flight consumers either complete within
the drain window or their unacknowledged delivery is returned safely for redelivery by RabbitMQ.
Consumers and application services must propagate `ConsumeContext.CancellationToken`.

## Recovery and replay

Every receive endpoint owner owns its `_error` and `_skipped` queues.

Replay requires:

1. an incident/change record and named owner;
2. root cause confirmation and deployed remediation;
3. compatibility verification against the stored message identity;
4. preservation of MessageId, CorrelationId, causation, and trace context;
5. verification of external-side-effect idempotency;
6. a bounded batch and rate;
7. observation of database state, produced messages, duplicates, queue depth, and new failures;
8. an explicit stop and rollback condition.

Automatic shovels from `_error` or `_skipped` back to the source queue are prohibited. Replay must not
invent identifiers to bypass duplicate detection.

See `docs/runbooks/messaging-operations.md` for queue investigation, backlog, dependency outage,
endpoint rename, breaking contract, capacity, and replay procedures.

## Automated evidence

The CI suite builds the custom RabbitMQ image and uses real RabbitMQ and PostgreSQL for broker- and
database-specific guarantees. It asserts state rather than merely asserting no exception.

Coverage includes:

- success;
- one and multiple immediate retries followed by success;
- delayed redelivery followed by success;
- retry/redelivery exhaustion and exact `_error` placement;
- permanent failure with no retry;
- skipped-message placement;
- exact custom metric increments;
- duplicate transport delivery and protected database side effects;
- bus-outbox rollback and commit behavior;
- correlation and causation propagation;
- business queue arguments and absence of receive TTL;
- RabbitMQ unavailable at startup;
- missing delayed-exchange capability;
- committed outbox recovery after broker/process interruption;
- collector database failure, stale-value behavior, and recovery;
- graceful consumer drain;
- consumer-policy validation and deterministic endpoint naming;
- contract serialization compatibility and distinct breaking identities;
- architecture boundary violations with actionable failure messages.

CI retains RabbitMQ image, broker status, plugin, application, build, and test diagnostics when a run
fails. The in-memory MassTransit harness is not accepted as evidence for RabbitMQ topology,
delayed-exchange behavior, PostgreSQL atomicity, broker recovery, or queue placement.
