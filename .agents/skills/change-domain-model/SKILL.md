---
name: change-domain-model
description: Plan, implement, or review aggregate, value-object, invariant, lifecycle, or semantic domain-error changes in a business service. Use when business rules or aggregate state transitions change. Keep domain code framework-free and failure-atomic.
---

# Change Domain Model

Load:

- `docs/agent-context/architecture/domain-boundary.md`;
- `docs/agent-context/architecture/testing.md`;
- owning domain source and domain tests.

Load `concurrency-idempotency.md` when aggregate version/idempotency behavior is involved, and `persistence.md` only if persistence mapping/schema must also change.

## Procedure

1. State the business invariant/change in technology-neutral language.
2. Identify the owning aggregate/value object.
3. Inspect existing domain creation/mutation/error patterns.
4. Implement the rule inside Domain without HTTP, EF Core, MediatR, FluentValidation, claims, database, or transport dependencies.
5. Ensure expected failures return semantic `Result` behavior and do not partially mutate state.
6. Update application translation only when contextual information outside the domain is required.
7. Add success, failure, boundary, and failure-atomicity tests.
8. Run domain-boundary architecture tests.

## Avoid

- moving invariants into validators/handlers because it is easier;
- exceptions for expected business outcomes;
- domain errors containing HTTP/database/security terminology;
- service/domain logic depending on sibling vertical slices;
- copying domain rules from another bounded context without requirements.

If requirements are insufficient to define the business invariant precisely, block/replan rather than inventing policy.