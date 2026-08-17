# Payment Service Context

`Payment.Api` owns provider-facing payment identity, reusable payment methods, setup orchestration, provider webhook reconciliation, and later money-movement behavior once an Order-to-Payment contract exists.

## Ownership

Payment owns:

- a small `PaymentCustomer` aggregate that maps the authoritative Customer-service `CustomerId` and authenticated identity to a provider customer;
- provider customer creation, performed lazily on the first provider operation;
- reusable `PaymentMethod` metadata required to identify and display a payment method safely;
- payment-method setup operations and their idempotency lifecycle;
- provider webhook verification, durable receipt, broker-backed retry/error handling, and reconciliation;
- Payment persistence, migrations, and provider-specific infrastructure.

Payment does not own customer profile data, addresses, Customer account lifecycle, order totals/state, inventory, pricing, PAN, CVC, wallet cryptograms, or provider identifiers in cross-service business contracts.

## Naming and model

`PaymentCustomer` is not a copy of the Customer aggregate and is not a generic account. It is Payment's local aggregate for the customer-to-payment-provider relationship. `CustomerId` is an immutable reference to the Customer bounded context. `ProviderCustomerId` is Payment-owned provider state and is created only when a provider operation needs it.

`PaymentMethod` is the reusable way the customer can pay. The domain does not call it `SavedPaymentMethod`: "saved" describes a UI/storage state rather than the business concept. The current capability supports card-backed payment methods, including provider-reported wallet metadata, and stores only display-safe metadata such as brand, last four digits, expiry, and wallet type.

Domain invariants return `Result`/`OperationError`. Expected conflicts do not use exceptions and failed operations do not partially mutate aggregate state.

## Customer identity synchronization

The browser never supplies an internal `CustomerId` to Payment.

Customer service publishes `CustomerIdentitySynchronized` through its PostgreSQL-backed transactional outbox after customer provisioning. Payment consumes that fact through its consumer inbox/outbox and creates the local `PaymentCustomer` from the authoritative `CustomerId`, identity provider, and identity subject. Rebinding an existing identity or CustomerId is a permanent integration failure.

The local `PaymentCustomer` may exist without a provider customer. This preserves Customer as the identity authority without coupling Customer provisioning to Stripe availability.

## Adding a payment method

1. The authenticated customer calls `POST /api/v1/payment-methods/setup` with a stable GUID `Idempotency-Key`.
2. Payment resolves the validated identity to an already synchronized `PaymentCustomer`; it never trusts a caller-supplied CustomerId.
3. Payment lazily creates/reuses the provider customer using a stable provider idempotency key derived from `PaymentCustomer.Id`.
4. Payment persists a local `PaymentMethodSetupOperation` before creating the external setup resource. The caller's idempotency key is the operation id and cannot be reused by another payment customer.
5. Payment creates/reuses a Stripe SetupIntent with the provider customer, `usage=off_session`, and card payment-method type.
6. The client confirms the SetupIntent using Stripe-hosted client tooling. PAN/CVC never transit Payment API.
7. Stripe attaches the successful PaymentMethod to the Stripe Customer as part of successful SetupIntent setup.
8. Stripe sends `setup_intent.succeeded` to `/webhooks/stripe`.
9. Payment verifies the signature over the untouched request payload with Stripe.net.
10. In one PostgreSQL commit, Payment inserts the deduplicated `PaymentWebhookEvent` receipt and a MassTransit bus-outbox command to process it. RabbitMQ availability is not on the webhook acknowledgement path.
11. The MassTransit consumer receives that command with the platform retry/redelivery/error-queue policy, retrieves current SetupIntent/payment-method state from Stripe, correlates it to the local setup operation, verifies provider-customer ownership, and upserts the local `PaymentMethod` in a short serializable transaction.
12. The first active method becomes default. Later preference changes use `PUT /api/v1/payment-methods/{id}/default`.

The webhook never attaches the method to the Stripe Customer. It reconciles local state after provider setup succeeded.

## Stripe boundary

Stripe.net is an infrastructure dependency only. `StripeClient`, Stripe services, Stripe DTOs, and `EventUtility.ConstructEvent` stay under `Infrastructure/Stripe`. Application features depend on provider-neutral `IPaymentProvider` and webhook contracts.

The webhook endpoint must read the untouched request payload because Stripe signature verification depends on the exact body. That HTTP-body plumbing remains at the infrastructure boundary; cryptographic verification and Stripe event parsing are performed by Stripe.net, not custom code.

## Reliability

External Stripe side effects are not atomically committed with PostgreSQL. Correctness therefore uses layered idempotency and reconciliation:

- durable setup-operation idempotency in PostgreSQL;
- stable Stripe idempotency keys for customer and SetupIntent creation;
- unique provider customer, payment-method, SetupIntent, and webhook event identifiers;
- MassTransit PostgreSQL bus outbox for atomic webhook receipt + processing-command dispatch;
- MassTransit/RabbitMQ retry, delayed redelivery, consumer concurrency, and error queues for asynchronous webhook processing;
- signature-verified, deduplicated webhook receipt retained as audit/reconciliation state;
- current-provider-state reconciliation rather than event-order assumptions;
- a short serializable reconciliation transaction with the durable webhook receipt as the redelivery idempotency fence;
- filtered unique database enforcement for one default payment method per payment customer.

Payment deliberately does not implement a second queue in PostgreSQL. There is no service-owned poller, processing lease, retry scheduler, or dead-letter state for Stripe webhooks; those delivery concerns belong to the messaging platform already operated by the repository.

A provider timeout is an unknown outcome, not proof of failure. Retrying the same business operation reuses the same idempotency key.

## Public surface

- `POST /api/v1/payment-methods/setup` — create or resume a future-use setup session.
- `GET /api/v1/payment-methods` — list active reusable methods for the authenticated payment customer.
- `PUT /api/v1/payment-methods/{id}/default` — change the preferred method.
- `POST /webhooks/stripe` — anonymous at the bearer layer but authenticated by Stripe's webhook signature.

## Deliberately not implemented yet

Order charging, `Payment`/`PaymentAttempt` money-movement aggregates, PaymentIntent execution, refunds, disputes, and Order-facing payment integration events are not invented here because the repository still has no approved Order bounded context or durable Order-to-Payment contract. When that contract exists, Order will own amount/purchase state while Payment owns provider execution and business payment outcomes, including a first-class `RequiresCustomerAction` outcome for off-session authentication fallback.
