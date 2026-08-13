---
name: change-messaging
description: Plan, implement, or review integration event/command contracts, routing, MassTransit outbox/inbox, retry/redelivery, queue topology, or messaging reliability changes. Use only when messaging behavior is actually affected; durable contract/topology changes require explicit human review.
---

# Change Messaging

Load:

- the scoped owner document selected by planning (typically `platform/shared-projects.md` for shared transport work or a service document for service-owned composition);
- `docs/agent-context/architecture/messaging.md`;
- relevant accepted messaging ADRs and nearest implementation/tests.

Detailed testing guidance is deferred to `$verify-dotnet-change`.

## Procedure

1. Classify the message as event (fact/fan-out) or command (one owning action).
2. Confirm bounded-context ownership and compatibility requirements.
3. Keep application/domain code behind `IIntegrationEventPublisher` or `IIntegrationCommandSender<TCommand>`.
4. Preserve scoped transactional outbox/inbox boundaries.
5. Preserve stable endpoint names and explicit command routing.
6. Treat contract serialization shape as durable.
7. Keep retry/redelivery conservative and limited to explicitly classified transient failures.
8. Update affected architecture/compatibility/routing/failure/recovery tests, then verify through `$verify-dotnet-change`.

## Stop conditions

Stop for explicit human review for a breaking integration contract, endpoint/queue rename or ownership change, unclear retry/failure-retention policy change, bypass of approved messaging abstractions, cross-service transaction/exactly-once assumptions not supported by the repository, or unexpected movement into another owner/project.

Do not expose MassTransit/RabbitMQ types to application/domain code to make implementation easier.