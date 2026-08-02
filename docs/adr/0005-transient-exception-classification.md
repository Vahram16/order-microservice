# ADR 0005: Conservative transient-exception classification

- Status: Accepted
- Date: 2026-08-02

## Context

Broadly retrying all timeouts, I/O errors, or statusless HTTP failures can repeat permanent defects or
external operations whose outcome is unknown.

## Decision

The shared classifier is default-deny and inspects stable provider data: PostgreSQL SQLSTATE, HTTP
status, socket error, explicit markers, and registered dependency rules. Unknown exceptions are
permanent. Cancellation is a separate disposition.

Generic `TimeoutException`, arbitrary `IOException`, statusless `HttpRequestException`, and HTTP 500
are not shared transient categories. Outcome-unknown operations are permanent unless a dependency
rule proves idempotency and safe replay.

Permanent evidence in a wrapped or aggregate exception takes precedence over transient evidence.

## Consequences

Services add narrow dependency rules instead of widening shared behavior. External side-effect
retries require an idempotency key and reconciliation procedure. Unit tests cover supported codes,
permanent categories, nested exceptions, conflicts, cancellation, and unknown failures.
