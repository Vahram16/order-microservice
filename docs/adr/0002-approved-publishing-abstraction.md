# ADR 0002: Approved publishing boundary

- Status: Accepted
- Date: 2026-08-02

## Context

Application code that injects `IBus` or raw RabbitMQ clients can bypass the scoped transactional
outbox and couple business behavior to the transport.

## Decision

Production application code publishes integration contracts through
`Microservices.Application.Messaging.IIntegrationMessagePublisher`.

The implementation is intentionally thin and delegates to scoped MassTransit `IPublishEndpoint`.
MassTransit owns normal consume-context propagation, correlation conventions, conversation identity,
and outbox participation. Explicit message, correlation, causation, or application headers are used
only when the caller has a concrete requirement.

Direct bus access remains available inside infrastructure composition and test infrastructure.

## Consequences

Application code has a stable transport-independent boundary without recreating a messaging
framework. New capabilities are added only when a real service requirement cannot be expressed with
MassTransit configuration or `ConsumerDefinition<TConsumer>`.
