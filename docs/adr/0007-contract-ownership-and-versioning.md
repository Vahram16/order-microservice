# ADR 0007: Integration contract ownership and versioning

- Status: Accepted
- Date: 2026-08-02

## Context

Duplicate message abstractions and independently redefined payloads let bounded contexts drift.
Embedding transport metadata or relying only on a mutable integer version couples contracts to one
transport and does not create a distinct breaking identity.

## Decision

`IIntegrationMessage` is canonical. Events implement `IIntegrationEvent`; commands implement
`IIntegrationCommand`. The publishing bounded context owns event contracts. Receivers reference the
published contract and do not redefine it.

Payloads exclude transport identity, correlation, causation, retry, and tracing metadata. Contracts
must not reference domain implementations, EF Core, APIs, consumers, or transport infrastructure.

Additive optional changes keep the existing identity. Breaking changes use a distinct CLR
namespace/type identity such as `.V2` and coexist during migration. Serializer options are explicit,
and historical payload tests prove supported compatibility.

## Consequences

Published fields are not renamed, removed, retyped, or given incompatible semantics. Breaking
versions require producer/consumer deployment ordering, rollback, and old-message drain planning.
