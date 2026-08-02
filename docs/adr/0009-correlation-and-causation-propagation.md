# ADR 0009: Correlation and causation propagation

- Status: Accepted
- Date: 2026-08-02

## Context

Requiring every consumer to copy correlation and parent-message metadata is inconsistent and easy to
forget. Putting transport metadata in payload contracts couples business schemas to MassTransit.

## Decision

The approved publisher assigns MessageId, propagates the consumed CorrelationId, and uses the parent
MessageId as MassTransit InitiatorId plus the bounded `x-causation-id` header. When no parent
correlation exists, the new MessageId becomes the correlation root.

An endpoint filter captures consumed-parent metadata for the scoped publisher. Application code may
supply explicit values only through validated `IntegrationPublishMetadata`; reserved transport
headers cannot be overridden.

## Consequences

Payload contracts remain transport-neutral. Correlation and causation are consistent for consumer-
produced messages without manual copying. Integration tests assert the exact parent/child values.
