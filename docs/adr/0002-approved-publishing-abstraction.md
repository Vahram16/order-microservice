# ADR 0002: Event and command boundaries with stable endpoint topology

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

Events and commands have different routing intent. Events describe facts and may fan out to many
subscribers. Commands request an action from one owning bounded context.

Direct application dependencies on MassTransit or RabbitMQ expose transport details and can bypass
the scoped bus outbox. Endpoint names and queue arguments are durable broker topology; deriving them
only from CLR consumer names makes ordinary refactoring an accidental topology change.

## Decision

Production application code uses two transport-independent boundaries:

- `IIntegrationEventPublisher` publishes `IIntegrationEvent` contracts by message type;
- `IIntegrationCommandSender<TCommand>` sends one `IIntegrationCommand` to its explicitly registered
  owning endpoint.

A producer registers each command destination once in infrastructure composition with
`AddIntegrationCommandRoute<TCommand>(endpointName)`. Application handlers never receive or construct
broker addresses. Duplicate route registration for the same command type fails immediately.

Business consumers use explicit stable lowercase kebab-case endpoint names. A consumer-class rename
must not rename its broker endpoint. Architecture tests prevent production application and domain
code from depending directly on bus, send, publish, or RabbitMQ transport types. Direct transport
access remains allowed in infrastructure composition and transport-focused tests.

## Consequences

Event publishers are unaware of subscriber endpoints. Command producers intentionally depend on the
stable identity of the command owner's endpoint, but that dependency is isolated in infrastructure
composition.

Changing a command destination, endpoint name, queue type, or immutable queue argument is a topology
migration. Deployment planning must cover producer and consumer ordering, old queue draining,
temporary coexistence when required, rollback, and obsolete topology removal.
