# ADR 0005: Integration contract ownership and versioning

- Status: Accepted
- Date: 2026-08-02
- Updated: 2026-08-04

## Context

Duplicate message abstractions and independently redefined payloads allow bounded contexts to drift.
Embedding broker identity or relying only on a mutable numeric version couples contracts to transport
behavior and does not create a distinct identity for a breaking change.

## Decision

`IIntegrationMessage` is the canonical integration marker. Events implement `IIntegrationEvent` and
commands implement `IIntegrationCommand`.

The bounded context that publishes an event owns that event contract. The bounded context that owns
a command operation owns the command contract; producers reference that contract rather than
redefining it locally.

Payloads exclude transport message identity, correlation, causation, retry state, tracing data, and
broker headers. Contract assemblies must not reference domain implementations, EF Core, APIs,
consumers, MassTransit, or RabbitMQ.

Additive optional changes may retain the existing contract identity. Breaking changes use a distinct
CLR type or namespace identity such as `.V2` and coexist during migration. Serializer behavior is
explicit, and historical payload tests define the supported compatibility surface.

## Consequences

Published fields are not renamed, removed, retyped, or assigned incompatible semantics in place.
Breaking versions require producer and consumer deployment ordering, rollback planning, and old
message draining before the previous contract is retired.
