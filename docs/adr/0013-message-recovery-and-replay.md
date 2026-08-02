# ADR 0013: Message recovery and replay

- Status: Accepted
- Date: 2026-08-02

## Context

Automatic replay can repeat external side effects, bypass duplicate detection, or create an
unbounded poison-message loop. Error and skipped queues need explicit operational ownership.

## Decision

The receive-endpoint owner owns `_error` and `_skipped` investigation and replay. There is no
automatic shovel back to the source queue.

Replay requires a named incident owner, root-cause remediation, contract compatibility, preserved
message/correlation/causation identity, side-effect idempotency verification, bounded batch and rate,
state and queue observation, and explicit stop/rollback criteria.

Skipped messages are investigated as routing, identity, topology, or deployment-order failures;
they are not treated as consumer exceptions. A parking queue is introduced only with a separately
approved expiration topology.

## Consequences

Operators use the checked-in messaging runbook. Replay cannot invent identifiers to defeat inbox
deduplication. Error and skipped retention remain evidence windows, not workflow storage.
