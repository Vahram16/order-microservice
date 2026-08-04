# ADR 0002: Separate event publication and command sending boundaries

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

Application code that injects `IBus`, `IPublishEndpoint`, `ISendEndpointProvider`, or raw RabbitMQ
clients can bypass the scoped transactional outbox and couple business behavior to transport details.
Events and commands also have different routing intent: events fan out to interested subscribers,
while a command targets one owning endpoint.

## Decision

Production application code uses two transport-independent boundaries:

- `IIntegrationEventPublisher` publishes `IIntegrationEvent` contracts through scoped MassTransit
  `IPublishEndpoint`;
- `IIntegrationCommandSender<TCommand>` sends one `IIntegrationCommand` through scoped MassTransit
  `ISendEndpointProvider` to the command's explicitly registered owning endpoint.

A producer registers each command route once in infrastructure composition with
`AddIntegrationCommandRoute<TCommand>(endpointName)`. Application handlers never receive or construct
broker addresses. Duplicate command-route registration fails immediately.

MassTransit owns normal consume-context propagation, correlation conventions, conversation identity,
and bus-outbox participation. Explicit message, correlation, causation, or application headers are
used only when a concrete workflow requires them.

Direct bus access remains available inside infrastructure composition and test infrastructure.

## Consequences

The application API makes event fan-out and command point-to-point intent explicit. Sending a command
creates deliberate destination coupling, so the endpoint name is stable infrastructure configuration
and changing it is a topology migration. No central command-routing registry or queue-name parameter
is exposed to application code.
