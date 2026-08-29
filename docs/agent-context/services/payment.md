# Payment Service Context

`Payment.Api` owns the customer-to-provider payment relationship, reusable payment methods, provider setup, Order payment attempts, Stripe execution, provider webhook reconciliation, and payment-domain outcomes.

## Ownership

Payment owns:

- `PaymentCustomer`, mapping authoritative CustomerId/identity to a provider customer;
- reusable `PaymentMethod` display-safe metadata;
- payment-method setup/idempotency and Stripe SetupIntent reconciliation;
- `OrderPaymentAttempt`, one durable provider-neutral payment attempt per Order;
- PaymentIntent creation/confirmation/cancellation and future capture/refund behavior when explicitly required;
- Stripe webhook signature verification, durable deduplication, provider-state reconciliation, and Payment persistence/migrations.

Payment does not own Customer profile/addresses, Order totals/lifecycle, Product pricing, Inventory, PAN/CVC, or Stripe identifiers in cross-service contracts.

## Reusable payment methods

The browser never supplies an internal CustomerId. Customer publishes authoritative identity synchronization; Payment creates its local `PaymentCustomer` and lazily creates the provider customer only when a provider operation needs it.

`POST /api/v1/payment-methods/setup` persists a `PaymentMethodSetupOperation` before external provider creation and uses stable Stripe idempotency keys. Stripe.net owns typed SetupIntent/PaymentMethod calls and exact-raw-body webhook verification. Successful setup is reconciled from current Stripe state and stores only display-safe card metadata.

## Order payment execution

Order sends provider-neutral `AuthorizeOrderPayment` with OrderId, authoritative CustomerId, PaymentMethodId, amount/currency, and deadline. Payment does not recalculate the commerce total.

Payment resolves the Customer-owned identity mapping and verifies that the selected active PaymentMethod belongs to that PaymentCustomer. It persists `OrderPaymentAttempt` **before** PaymentIntent creation; OrderId is unique so duplicate/redelivered authorization commands converge on one logical attempt. External creation/confirmation/cancellation uses stable provider idempotency keys derived from OrderId.

Provider states are translated into durable business outcomes:

- customer authentication needed -> `PaymentActionRequired`;
- provider authorization available (`requires_capture`) -> `PaymentAuthorized`;
- payment method/business rejection -> `PaymentRejected`;
- cancellation -> `PaymentCancelled`.

Order and RabbitMQ contracts never contain Stripe PaymentIntent IDs, Stripe Customer IDs, client secrets, or Stripe status strings.

## 3-D Secure/customer action

`RequiresCustomerAction` is normal payment state, not an infrastructure failure. Order moves to its awaiting-customer-action state and keeps its inventory reservation until the checkout deadline.

The authenticated customer obtains current provider action data only through `GET /api/v1/payment-attempts/{id}/action`. Payment verifies PaymentCustomer ownership and current provider state before returning the client secret. Client secrets are not stored in Order, message payloads, URLs, or logs.

The browser/Stripe SDK performs the challenge, but the browser is never authoritative for payment success. Stripe sends a signed PaymentIntent webhook; Payment deduplicates the webhook, re-fetches current PaymentIntent state, verifies customer/method/amount/currency ownership, updates `OrderPaymentAttempt`, and publishes the provider-neutral outcome through its PostgreSQL bus outbox. That event—not a browser callback—resumes Order.

## Reliability

- PostgreSQL uniqueness fences duplicate setup, webhook, and Order-payment operations;
- optimistic concurrency protects mutable Payment aggregates;
- MassTransit PostgreSQL bus/consumer outbox closes local DB/message gaps;
- provider I/O remains outside database atomicity and therefore uses stable idempotency plus authoritative re-fetch/reconciliation;
- webhook receipt is a durable idempotency fence and RabbitMQ retry/redelivery/error queues own asynchronous delivery policy;
- late provider authorization after Order compensation is cancelled when it remains uncaptured; impossible/already-captured late states are surfaced for explicit reconciliation rather than silently mutating Order;
- `RefundPending` remains automatically reconciled from authoritative provider state. A provider refund that becomes `failed` or `canceled` is persisted as `RefundFailed` and emits a Critical `RefundRequiresManualReconciliation` log after the durable save. It is deliberately not blindly retried: Stripe treats a failed refund as requiring an alternative customer reimbursement path. Operations must alert on this event and close the financial obligation through the approved reconciliation runbook.

The Order-payment flow currently authorizes with manual capture. Capture timing is a separate fulfillment/business policy and must not be invented inside Order or Stripe infrastructure; a future explicit capture requirement extends the Payment contract.

## Public surface

- `POST /api/v1/payment-methods/setup`
- `GET /api/v1/payment-methods`
- `PUT /api/v1/payment-methods/{id}/default`
- `GET /api/v1/payment-attempts/{id}/action`
- `POST /webhooks/stripe` (bearer-anonymous, Stripe-signature authenticated)

## Context routing

- Payment API slice -> `../architecture/vertical-slice.md` / `api-and-errors.md`;
- Payment aggregate -> `../architecture/domain-boundary.md`;
- persistence/idempotency -> `../architecture/persistence.md` / `concurrency-idempotency.md`;
- Order/payment commands/events -> `../architecture/messaging.md`;
- resource ownership -> `../architecture/security.md`;
- verification -> `../testing-map.md`.
