# ADR 0003: Prohibit direct bus and broker access

- Status: Accepted
- Date: 2026-08-02

## Context

A written convention cannot prevent a future constructor from accepting `IBus`, `IBusControl`,
`ISendEndpointProvider`, a RabbitMQ connection, or a broker channel.

## Decision

Production controllers, handlers, services, domain types, application interfaces, and consumers must
not depend on direct MassTransit bus or RabbitMQ transport types. Domain and contract assemblies must
remain independent from transport and messaging persistence.

Reflection-based architecture tests scan every relevant production assembly. Important rules include
a negative fixture proving violations are detected with actionable output.

## Consequences

Infrastructure composition may still configure MassTransit and RabbitMQ. Test fixtures may publish
directly when exercising broker behavior. Production features must use the approved application
publisher or a separately reviewed outbox-aware abstraction.
