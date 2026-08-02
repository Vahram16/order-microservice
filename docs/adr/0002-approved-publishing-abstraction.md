# ADR 0002: Approved publishing abstraction

- Status: Accepted
- Date: 2026-08-02

## Context

Injecting `IBus`, `IBusControl`, raw RabbitMQ clients, or transport-specific endpoint providers lets
application code bypass the transactional outbox and transport metadata policy.

## Decision

Production application code publishes only through
`Microservices.Application.Messaging.IIntegrationMessagePublisher`.

The scoped infrastructure implementation uses `IPublishEndpoint`, propagates cancellation,
MessageId, CorrelationId, parent causation, and bounded application headers, and rejects application
overrides of transport-owned headers.

Direct bus access is limited to infrastructure composition and explicitly named test infrastructure.
Architecture tests scan production assemblies and report the violating type, forbidden dependency,
and approved alternative.

## Consequences

The abstraction is not a cosmetic rename: it owns metadata, header validation, outbox participation,
and architectural enforcement. New publishing capabilities must be added here only when they retain
those invariants.
