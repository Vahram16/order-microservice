# Messaging reliability operations guide

This guide describes how to use and operate the shared RabbitMQ/MassTransit baseline provided by
`AddRabbitMqWithPostgresOutbox<TDbContext>`. Durable architectural choices are recorded separately:

- [ADR 0001: Transactional messaging with bus and consumer outboxes](adr/0001-transactional-bus-and-consumer-outbox.md)
- [ADR 0002: Event and command boundaries with stable endpoint topology](adr/0002-approved-publishing-abstraction.md)
- [ADR 0004: Bounded retry, delayed redelivery, and failure classification](adr/0004-retry-and-delayed-redelivery-policy.md)
- [ADR 0006: Queue durability, capacity, and failure retention](adr/0006-queue-capacity-retention-and-parking.md)
- [ADR 0007: Integration contract ownership and versioning](adr/0007-contract-ownership-and-versioning.md)

## Register the baseline

Each service registers its own DbContext, consumers, and stable endpoint prefix:

```csharp
builder.Services.AddRabbitMqWithPostgresOutbox<ServiceDbContext>(
    builder.Configuration,
    "inventory",
    registration =>
    {
        var consumer = registration.AddConsumer<ReserveInventoryConsumer>();
        consumer.Endpoint(endpoint => endpoint.Name = "inventory-reserve");
    });
```

The DbContext model must include the MassTransit inbox and outbox entities:

```csharp
modelBuilder.AddMassTransitOutboxEntities();
```

Run service-owned schema migrations before application replicas start.

## Publish events and send commands

Events fan out by contract type:

```csharp
await eventPublisher.PublishAsync(new OrderSubmitted(...), cancellationToken: cancellationToken);
```

Commands target one owning endpoint. Register the route in producer infrastructure composition:

```csharp
builder.Services.AddIntegrationCommandRoute<ReserveInventory>("inventory-reserve");
```

Application code injects the typed sender and never handles a queue or exchange address:

```csharp
await commandSender.SendAsync(new ReserveInventory(...), cancellationToken: cancellationToken);
```

Both APIs use the scoped bus outbox. Publishing or sending does not make the message durable by
itself; the configured DbContext work must be saved and its transaction committed. A rollback must
produce no broker message.

Use explicit message, correlation, causation, or application headers only for a concrete workflow.
Transport identity, retry state, tracing data, and broker headers do not belong in business payloads.

## Endpoint and topology rules

- Give every durable consumer an explicit stable lowercase kebab-case endpoint name.
- Keep a command route identical to the owning consumer endpoint name.
- Treat endpoint renames, queue-type changes, and immutable queue-argument changes as migrations.
- Deploy and verify owning command topology before producers begin sending to a new destination.
- Use endpoint-name overrides only for simple operational tuning.
- Use `ConsumerDefinition<TConsumer>` for other consumer-specific behavior, but do not add a second
  retry or delayed-redelivery middleware stack.

A topology migration plan should cover deployment order, old queue draining, temporary old/new
coexistence when required, rollback, and obsolete topology removal.

## Retry and failure classification

The shared policy applies short bounded immediate retries followed by bounded broker-backed delayed
redelivery. Only explicitly transient failures are retried.

Use the shared markers when the application owns the exception type:

- `ITransientConsumerFailure`
- `IPermanentConsumerFailure`
- `IOutcomeUnknownConsumerFailure`

Use a narrow singleton `IConsumerExceptionRule` for dependency exceptions that the application does
not own. Classify a dependency failure as transient only when provider evidence is stable and the
operation is safe to replay. Unknown, permanent, outcome-unknown, and cancelled failures are not
retried by default.

Review HTTP, database, and client-library retry policies together with message retry so the combined
attempt count remains bounded and intentional.

## Queue and failure handling

Business receive queues are durable quorum queues by default and intentionally have no
`x-message-ttl`. They use bounded message count and bytes with `reject-publish` overflow. Error and
skipped queues have separate bounded retention and capacity.

Operational response:

- `_error` contains messages whose consumer execution ended terminally;
- `_skipped` contains messages delivered to an endpoint with no matching consumer;
- sustained queue-capacity rejection indicates consumer throughput or dependency health problems;
- replay from failure queues is an operator-owned action and must preserve idempotency.

Do not add a generic parking queue or business-message expiration without a dedicated design for DLX
routing, retention, ownership, observability, and replay.

## Broker and deployment requirements

- Use the repository RabbitMQ image or another image that includes the delayed-message exchange
  plugin required by delayed redelivery.
- Configure production TLS and secret-managed credentials.
- Keep broker message-size configuration aligned with the application deployment configuration.
- Verify the delayed-exchange plugin and RabbitMQ Prometheus endpoint during deployment smoke tests.
- Do not probe plugin availability by opening a second application-owned RabbitMQ connection.

## Observability

MassTransit OpenTelemetry is the primary transport signal. The shared package also emits consumer
retry, redelivery, failure, and attempt-duration metrics plus PostgreSQL outbox backlog, oldest-age,
and collection-failure signals.

Monitor at least:

- RabbitMQ availability and connection churn;
- `_error` and `_skipped` queue depth;
- sustained consumer retry/redelivery rate;
- outbox backlog and oldest pending-message age;
- queue capacity and publish rejection;
- consumer throughput and processing latency.

Metrics-collection failure is observable but does not control application readiness. Alert thresholds
should be tuned against real workload and SLO evidence.

## Validation evidence

The automated suite uses real RabbitMQ and PostgreSQL to verify:

- event fan-out and command point-to-point routing;
- bus-outbox commit, rollback, drain, and interruption recovery;
- consumer duplicate suppression around protected database effects;
- bounded immediate retry and delayed redelivery;
- terminal `_error` and `_skipped` routing;
- RabbitMQ and PostgreSQL recovery behavior;
- queue capacity and absence of business receive TTL;
- bounded startup and graceful in-flight consumer shutdown;
- architecture boundaries that prevent application and domain transport leakage.

Service-specific consumers should add tests for their own idempotency, dependency classification,
ordering, concurrency, and contract compatibility requirements.
