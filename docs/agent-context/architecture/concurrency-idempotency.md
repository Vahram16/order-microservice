# Concurrency, Idempotency, and Transaction Semantics

These rules are project-specific. Do not replace them with a generic retry or idempotency pattern without explicit requirements and architecture review.

## Optimistic concurrency

Customer uses a strong ETag derived from aggregate version. For state-changing operations (except initial provisioning):

```text
HTTP If-Match
    ↓
CustomerHttp.ReadExpectedVersion
    ↓
Command.ExpectedVersion
    ↓
Aggregate.EnsureExpectedVersion(...)
    ↓
Domain mutation increments/uses aggregate Version
    ↓
EF Core concurrency token on Customer.Version
    ↓
SaveChangesAsync
```

The two layers are intentional:

- application/domain expected-version validation gives deterministic stale-client semantics before mutation;
- EF Core's concurrency token protects overlapping database transactions.

Do not remove either layer merely because the other exists.

Customer's documented HTTP semantics are:

- missing required `If-Match` -> `428 Precondition Required`;
- malformed ETag -> `400 Bad Request`;
- stale/concurrent version -> `412 Precondition Failed`.

## Address idempotency

Adding an address uses a stable GUID `Idempotency-Key`. The key becomes the address identity.

```text
Idempotency-Key (GUID)
    ↓
AddressId
    ↓
existing address?
    ├─ no -> continue mutation
    └─ yes
        ├─ semantically same data -> return existing success
        └─ different data -> customer.idempotency_key_reused
```

The application layer interprets the identity as an API idempotency key. The domain only knows address identity and semantic equality; it does not know HTTP idempotency terminology.

## Concurrency recovery for idempotent create

`AddingAddress/V1/AddCustomerAddressHandler.cs` is the canonical complex mutation. It handles concurrency/unique-key races by:

1. rolling back the transaction;
2. clearing the EF change tracker;
3. reloading the aggregate;
4. finding the address by stable identity;
5. comparing semantic data;
6. returning existing success or idempotency-key-reused semantics.

This is not a generic instruction to catch every `DbUpdateException`. Only known constraints with defined business semantics should be translated.

## Transaction boundary

When a business operation changes multiple rows/effects that must succeed together, use one explicit database transaction. In Customer address mutations, default-address conflict clearing, aggregate mutation, audit creation, and persistence are coordinated as one unit.

On expected business failure after transaction start, explicitly roll back where the existing pattern requires it. Unknown exceptions should not be converted into misleading client outcomes.

## Retry rule

Do not add broad application retries around non-idempotent business mutations. Provider execution strategies may be used where the existing implementation deliberately makes the operation retry-safe. Preserve the operation's idempotency and transaction semantics when doing so.

## Persistence constraints as final guards

Database unique/concurrency constraints are final race-condition guards, not substitutes for domain invariants. The application may translate a known constraint only when the exact constraint has an intentional semantic mapping.

## Canonical evidence

- `AddingAddress/V1/AddCustomerAddressHandler.cs`;
- `UpdatingAddress/V1/UpdateCustomerAddressHandler.cs`;
- `Provisioning/V1/ProvisionCustomerHandler.cs`;
- `CustomerHttp.cs` for ETag parsing/writing;
- `CustomerDbContext.cs` for the EF concurrency token and uniqueness constraints;
- `CustomerDatabaseConstraints.cs`;
- `PostgresExceptionExtensions.cs`;
- `docs/customer-service.md`;
- `CustomerApiIntegrationTests.cs`;
- `CustomerPersistenceModelTests.cs`;
- `CustomerFlowReviewTests.cs`.

## Review questions

1. Can the operation safely run twice after an unknown transport/process outcome?
2. Is the expected-version check still performed before mutation?
3. Does EF still enforce the concurrency token?
4. Are known uniqueness races translated only by named constraints?
5. Does failure leave both aggregate state and persistence effects atomic?
6. If a retry/reload path exists, does it distinguish same semantic operation from conflicting reuse?
7. Is HTTP idempotency terminology kept out of the domain?