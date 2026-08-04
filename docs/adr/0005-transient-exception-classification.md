# ADR 0005: Service-owned transient exception classification

- Status: Accepted
- Date: 2026-08-02

## Context

Retry safety depends on the operation and its idempotency, not only on a broad exception category.
A shared list of HTTP statuses, socket codes, or database states can silently apply the wrong policy
to a future dependency.

## Decision

The shared classifier is default-deny. It understands only explicit transient, permanent,
outcome-unknown, and cancellation markers, plus registered `IConsumerExceptionRule` implementations.

Each service owns narrow rules for the dependencies it calls. A rule must be based on stable provider
information and must be added only when the operation is safe to replay. Unknown exceptions are
permanent.

Permanent or cancelled evidence in a wrapped or aggregate exception takes precedence over transient
evidence.

## Consequences

The shared package remains small and predictable. Services document and test their own retry rules,
as demonstrated by the PostgreSQL recovery integration test. External side-effect retries still
require idempotency and reconciliation.
