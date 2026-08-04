# Messaging reliability baseline

This document describes the shared behavior provided by
`AddRabbitMqWithPostgresOutbox<TDbContext>`. The shared package defines a safe baseline; individual
services own business-specific delivery policy.

## Core guarantees

The baseline provides:

- MassTransit over RabbitMQ;
- PostgreSQL Entity Framework bus outbox and consumer inbox/outbox;
- bounded immediate retry followed by bounded broker-backed delayed redelivery;
- default-deny exception classification;
- durable quorum queues by default;
- bounded queue count and byte capacity with `reject-publish` overflow;
- independently retained `_error` and `_skipped` queues;
- lightweight OpenTelemetry and outbox backlog signals;
- bounded startup and graceful consumer shutdown.

## Publishing boundary

Application code publishes integration contracts through
`IIntegrationMessagePublisher`. Its implementation is deliberately thin and delegates to scoped
MassTransit `IPublishEndpoint`, preserving bus-outbox participation.

MassTransit owns normal consume-context propagation, correlation conventions, conversation identity,
and transport behavior. Callers may explicitly provide message, correlation, causation, or bounded
application headers only when a concrete workflow requires them.

Transport identity, retry state, tracing data, and broker headers remain outside business payloads.

## Retry ownership

The shared policy retries only exceptions explicitly classified as transient. Unknown,
permanent, outcome-unknown, and cancelled failures are not retried.

The shared classifier understands explicit marker interfaces and registered
`IConsumerExceptionRule` implementations. It intentionally does not guess retry safety from broad
HTTP, socket, timeout, or database exception categories.

Each service owns narrow dependency rules because retry safety depends on operation idempotency and
side effects. Services with materially different retry, redelivery, concurrency, or rate-limit needs
should configure the consumer through MassTransit `ConsumerDefinition<TConsumer>` or a narrow
endpoint override.

## Queue topology

Durable business receive queues do not use `x-message-ttl`. Expiring a message from a receive queue
can remove business work before MassTransit can place it in `_error` or `_skipped`.

Business queues use:

- durable, non-auto-delete topology;
- quorum queues by default;
- bounded message count and bytes;
- `reject-publish` overflow;
- a bounded delivery limit.

Error and skipped queues have separate retention and capacity.

RabbitMQ queue arguments are durable topology. Changing a queue name, type, or immutable argument
requires a controlled migration because RabbitMQ rejects inequivalent redeclaration.

## Endpoint identity

Consumers use explicit stable lowercase kebab-case endpoint names through standard MassTransit
registration. Renaming a CLR consumer type must not rename its broker endpoint.

Optional endpoint-name configuration overrides support simple operational tuning. They are not a
mandatory policy registry. Rich consumer-specific behavior belongs in `ConsumerDefinition<T>` near
the owning service.

## Contracts

`IIntegrationMessage` is the canonical marker. Events implement `IIntegrationEvent`; commands
implement `IIntegrationCommand`.

Contracts remain independent from domain implementations, Entity Framework, APIs, consumers, and
transport libraries. Additive optional changes may retain the same identity. Breaking changes use a
distinct CLR type or namespace such as `.V2` and coexist during migration.

Serializer settings are explicit and historical payload tests cover supported compatibility.

## Observability and health

MassTransit OpenTelemetry remains the primary transport and consumer signal. The shared package adds
lightweight consumer retry/failure/duration metrics and PostgreSQL outbox backlog/age metrics.

A metrics collection failure is observable but does not control application readiness. Business
readiness is based on dependencies required to serve or process work, not on whether an observability
query succeeded.

Performance indexes, dashboard complexity, and alert thresholds are introduced only after a real
service workload and SLO justify them.

## Delayed exchange capability

Delayed redelivery requires the RabbitMQ delayed-message exchange plugin. The deployment image and
infrastructure smoke tests own capability verification. Application startup does not create a second
raw RabbitMQ connection solely to probe the plugin.

## Automated evidence

The integration suite uses real RabbitMQ and PostgreSQL to verify externally meaningful behavior:

- successful consumption;
- bounded immediate retry;
- delayed redelivery;
- exhausted and permanent failures reaching `_error`;
- unsupported messages reaching `_skipped`;
- duplicate transport IDs not repeating protected database effects;
- bus-outbox commit and rollback behavior;
- committed outbox recovery after broker interruption;
- RabbitMQ client recovery;
- service-owned PostgreSQL transient classification;
- queue capacity and absence of business receive TTL;
- graceful in-flight consumer drain.

Architecture tests enforce transport-independent contracts and prevent application/domain layers
from directly depending on RabbitMQ or MassTransit bus abstractions.
