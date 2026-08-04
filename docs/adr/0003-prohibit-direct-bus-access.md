# ADR 0003: Prohibit direct bus and broker access

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

A written convention cannot prevent a future constructor from accepting `IBus`, `IBusControl`,
`IPublishEndpoint`, `ISendEndpointProvider`, a RabbitMQ connection, or a broker channel.

## Decision

Production controllers, handlers, services, domain types, application interfaces, and consumers must
not depend on direct MassTransit bus or RabbitMQ transport types. Domain and contract assemblies must
remain independent from transport and messaging persistence.

Events use `IIntegrationEventPublisher`. Commands use the closed generic
`IIntegrationCommandSender<TCommand>` registered for their single owning endpoint. Reflection-based
architecture tests scan every relevant production assembly and include a negative fixture proving
violations are detected with actionable output.

## Consequences

Infrastructure composition may still configure MassTransit, RabbitMQ, and command destinations. Test
fixtures may use transport APIs directly when exercising broker behavior. Production features cannot
bypass the event/command boundaries or the scoped bus outbox.
