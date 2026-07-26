# Notifications service and Postmark

`Notifications.Api` is a separately deployable service. Identity owns account tokens and durable notification intent; Notifications owns provider delivery. Postmark types, credentials, template aliases, responses, and retry classification must not enter Identity feature code or shared business contracts.

## Runtime flow

1. Identity writes an encrypted notification outbox row in the same transaction as the account operation.
2. Identity's outbox dispatcher posts a provider-neutral payload to `POST /internal/v1/notifications/identity` with a bearer secret and `Idempotency-Key` header.
3. Notifications validates the secret, idempotency key, template, recipient, HTTPS action URL, and expiry.
4. Notifications encrypts the recipient and action URL with ASP.NET Core Data Protection, persists the delivery, and returns `202 Accepted`.
5. A singleton hosted worker creates a scope and invokes a scoped dispatcher.
6. The dispatcher leases rows with PostgreSQL `FOR UPDATE SKIP LOCKED`, decrypts the payload, maps the business template to a Postmark template alias, and calls Postmark's template API.
7. Provider acceptance records the Postmark message ID and clears the encrypted payload. Permanent failures or exhausted/expired retries are dead-lettered and also clear the payload.

Identity retries only until Notifications durably accepts the request. Notifications alone owns retries after that boundary.

## Supported Identity templates

| Business template | Postmark alias setting |
| --- | --- |
| `identity.email-confirmation` | `Postmark:EmailConfirmationTemplateAlias` |
| `identity.password-reset` | `Postmark:PasswordResetTemplateAlias` |

Both Postmark templates receive this model:

```json
{
  "actionUrl": "https://...",
  "expiresAtUtc": "2026-07-27T12:00:00+00:00"
}
```

Templates should clearly display the expiry, avoid embedding secrets outside the action URL, and include the expected product support and security copy.

## Security invariants

- The ingress endpoint has no browser CORS policy and accepts only a bounded JSON body.
- The bearer webhook secret is compared in constant time and must contain at least 32 characters.
- Production rejects the repository's development ingress key and Postmark's test token.
- The idempotency key must match the source event ID.
- Reusing an event ID with a different immutable payload returns `409 Conflict`.
- Only allow-listed templates and safe absolute action URLs are accepted.
- Recipient and action URL are stored only inside a Data Protection payload and are removed after provider acceptance or dead-lettering.
- Postmark response bodies and tokens are not logged or retained.
- Notifications owns a separate database and must never read the Identity database.

Data Protection keys are persisted in `notifications.data_protection_keys`. Production must protect the database and its backups using platform encryption and access controls. A future key-management integration may additionally protect Data Protection keys with an external key-encryption key without changing feature contracts.

## Delivery semantics

The boundary is at-least-once. The service prevents duplicate acceptance inside Notifications using unique source-event and idempotency indexes. A provider timeout can be ambiguous because a remote provider may accept a request before the connection fails; Postmark does not become part of the local transaction. Templates and business workflows must therefore tolerate the rare possibility of a duplicate transactional email.

Transient failures are HTTP `408`, `429`, network/timeouts, and `5xx`. They use bounded exponential retry until either:

- `NotificationDelivery:MaximumAttempts` is reached, or
- the account token expires before the next attempt.

Other provider rejections, invalid protected payloads, unsupported templates, and expired notifications are dead-lettered immediately.

## Required production configuration

```text
ConnectionStrings__notifications-db
NotificationsIngress__ApiKey
Postmark__ServerToken
Postmark__FromAddress
Postmark__MessageStream
Postmark__EmailConfirmationTemplateAlias
Postmark__PasswordResetTemplateAlias
```

The Aspire AppHost exposes development parameters for the ingress secret, Postmark server token, and sender address. The default Postmark token is `POSTMARK_API_TEST`, which validates request construction without sending live email. Replace it through user secrets or deployment configuration to test real delivery.

## Operations

- Run `Notifications.Migrator` before starting `Notifications.Api`.
- Scale API replicas horizontally; database leasing coordinates workers.
- Alert on dead-letter log event `2102`, sustained deferred event `2101`, health-check failures, and growing pending delivery counts.
- Rotate the ingress secret by coordinating Identity and Notifications configuration.
- Rotate Postmark server tokens through the secret store; never commit them.
- Use squash merge for connector-generated file-move or multi-file implementation histories.
