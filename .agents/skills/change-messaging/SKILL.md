---
name: change-messaging
description: Plan, implement, or review integration event/command contracts, routing, MassTransit outbox/inbox, retry/redelivery, queue topology, or messaging reliability changes. Use only when messaging behavior is actually affected; durable contract/topology changes require explicit human review.
---

# Change Messaging

Load:

- `docs/agent-context/architecture/messaging.md`;
- `docs/agent-context/architecture/testing.md`;
- relevant accepted messaging ADRs and nearest implementation/tests.

## Procedure

1. Classify the message as event (fact/fan-out) or command (one owning action).
2. Confirm bounded-context ownership and compatibility requirements.
3. Keep application/domain code behind `IIntegrationEventPublisher` or `IIntegrationCommandSender<TCommand>`.
4. Preserve the scoped transactional outbox/inbox boundary.
5. Preserve stable endpoint names and explicit command routing.
6. Treat contract serialization shape as durable.
7. Keep retry/redelivery conservative and limited to explicitly classified transient failures.
8. Update architecture, compatibility, routing, failure, recovery, and real broker/database tests as applicable.

## Stop conditions

Stop for explicit human review when the task requires:

- a breaking integration contract;
- endpoint/queue rename or ownership change;
- retry/failure-retention policy change with unclear operational effects;
- bypassing the approved application messaging abstraction;
- a cross-service transaction/exactly-once assumption not supported by the repository.

Do not expose MassTransit/RabbitMQ types to application/domain code to make implementation easier.