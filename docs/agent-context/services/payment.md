# Payment Service Ownership

`Payment.Api` owns payment-provider integration and the platform's saved-payment-method lifecycle.

## Owns

- the mapping from authoritative Customer-service `CustomerId` to provider customer identity;
- Stripe Customer creation, performed lazily on the first provider operation;
- saved payment-method projections containing provider identifiers and non-sensitive display metadata only;
- SetupIntent creation for future off-session use;
- Stripe webhook signature verification, durable deduplication, retry leasing, and reconciliation;
- Payment-service persistence, migrations, and provider-specific infrastructure.

## Does not own

- customer profile, email, addresses, or customer lifecycle state;
- PAN, CVC, raw card data, or other PCI-sensitive payment credentials;
- order totals, order state, fulfillment, inventory, or pricing;
- Stripe identifiers in cross-service business contracts.

## Customer identity synchronization

The browser never supplies an internal `CustomerId` to Payment. Customer service publishes `CustomerIdentitySynchronized` after idempotent customer provisioning. Payment consumes that fact and records `CustomerId` plus the authenticated identity-provider subject. This keeps Customer as the authority for customer identity while allowing Payment endpoints to resolve the authenticated principal locally.

The local `PaymentCustomer` row may exist before a Stripe Customer exists. `StripeCustomerId` is created lazily when a payment operation requires it. Concurrent creation is safe because all attempts use the same Stripe idempotency key derived from the internal customer id, and the local mapping is protected by optimistic concurrency and uniqueness.

## Adding a payment method

1. Authenticated customer calls `POST /api/v1/payment-methods/setup` with a stable GUID `Idempotency-Key`.
2. Payment resolves the authenticated identity to its synchronized `PaymentCustomer`.
3. Payment lazily creates/reuses the Stripe Customer.
4. Payment creates a Stripe SetupIntent for `usage=off_session`, with the provider customer and internal correlation metadata.
5. The client confirms the SetupIntent using Stripe-hosted UI. Card data never reaches Payment API.
6. Stripe attaches the resulting PaymentMethod to the Stripe Customer after successful setup.
7. `setup_intent.succeeded` reaches `/webhooks/stripe`; Payment verifies the Stripe signature and durably records the event.
8. The webhook processor retrieves current provider state, upserts the local saved-payment-method projection, and applies default-method policy.

The webhook does not attach a card to the Stripe Customer. It reconciles local state after Stripe has completed the setup.

## Reliability

Stripe calls that create provider resources use stable business idempotency keys. Stripe webhooks are treated as duplicate and out-of-order external messages. Supported events are persisted before acknowledging success. Processing uses a database lease so multiple replicas can safely compete, with retry after lease expiry/failure backoff. The local database never assumes a provider network timeout means failure.

Customer integration facts use the repository's PostgreSQL-backed MassTransit outbox/inbox so Customer state and publication are durable and Payment consumption is duplicate-safe.

## Future order payment boundary

A future Order-to-Payment slice should create an internal Payment and execute a PaymentIntent using a saved method. That work is deliberately not included here because Order service and its durable payment contract do not yet exist. Order will own amount/purchase state; Payment will own provider execution and publish business payment outcomes. `RequiresCustomerAction` must be a first-class outcome because off-session authentication can still be required.
