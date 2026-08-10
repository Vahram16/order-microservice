# Messaging Architecture

The messaging boundary is intentionally transport-independent at application/domain level and reliability-aware at infrastructure level.

## Application contracts

Production application code uses:

- `IIntegrationEventPublisher` for facts that may fan out to multiple subscribers;
- `IIntegrationCommandSender<TCommand>` for a request sent to one explicitly configured owning endpoint.

Both are defined in `src/Shared/Microservices.Application/Messaging/IIntegrationMessaging.cs` and use framework-free contracts from `Microservices.Contracts`.

Application/domain code must not depend directly on:

- `IBus`;
- `IPublishEndpoint`;
- `ISendEndpointProvider`;
- RabbitMQ clients/addresses;
- transport queue/exchange configuration.

## Event vs command intent

Use an integration event when publishing a fact that interested bounded contexts may consume independently.

Use an integration command when requesting one owning bounded context to perform an action. The producer registers the stable destination once in infrastructure composition with `AddIntegrationCommandRoute<TCommand>(endpointName)`.

Do not turn events into directed commands or commands into broadcast events merely to simplify wiring.

## Transactional delivery

Services using the approved messaging infrastructure rely on PostgreSQL-backed MassTransit bus outbox and consumer inbox/outbox semantics.

A message participates in the local database transaction only when emitted through the configured scoped boundary associated with the owning `DbContext`. External side effects and other databases remain outside that atomic boundary and require their own idempotency/reconciliation design.

Delivery is not a global exactly-once guarantee. Handlers must remain safe for the documented delivery model and duplicate-detection window.

## Contracts

Integration contracts are framework-free and durable. Treat serialized shape and endpoint topology as compatibility boundaries.

Do not place transport metadata such as broker headers/addresses in business payloads. `IntegrationMessageMetadata` is the approved optional transport metadata boundary.

Before modifying a message contract, read `docs/adr/0005-integration-contract-ownership-and-versioning.md` and inspect serialization compatibility tests.

## Retry and failure delivery

Retry/redelivery is conservative and default-deny. Service-owned rules may classify known transient dependency failures. Do not add broad retry for arbitrary exceptions or business validation failures.

Business queues are durable quorum queues with bounded capacity policy; `_error` and `_skipped` handling are intentionally retained. Receive-queue TTL is intentionally not part of the baseline.

Before changing retry, redelivery, capacity, queue type, endpoint names, or failure retention, read the accepted messaging ADRs and `docs/messaging-failure-delivery-policy.md`.

## Stable topology

Endpoint names are infrastructure contracts. Refactoring a consumer class must not silently rename its durable endpoint.

Command routes must have one owning endpoint. Duplicate route registration is invalid.

## Canonical evidence

- `src/Shared/Microservices.Application/Messaging/IIntegrationMessaging.cs`;
- `src/Shared/Microservices.Contracts/`;
- `src/Shared/Microservices.Messaging/`;
- `docs/adr/0001-transactional-bus-and-consumer-outbox.md`;
- `0002-approved-publishing-abstraction.md`;
- `0003-retry-redelivery-and-failure-classification.md`;
- `0004-queue-durability-capacity-and-failure-retention.md`;
- `0005-integration-contract-ownership-and-versioning.md`;
- `docs/messaging-failure-delivery-policy.md`;
- `tests/Microservices.ArchitectureTests/MessagingArchitectureTests.cs`;
- `tests/Microservices.Messaging.Tests/`.

## Review questions

1. Is this event or command owned by the correct bounded context?
2. Is application code using only the approved transport-independent boundary?
3. Does the message participate in the intended database/outbox transaction?
4. Is the contract backward-compatible for existing consumers?
5. Is the endpoint name stable and explicitly owned?
6. Are retry rules bounded to known transient failures?
7. Are duplicate/unknown-outcome scenarios safe?
8. Does the change require operational/observability updates?