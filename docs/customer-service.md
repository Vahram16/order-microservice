# Customer service

`Customer.Api` is the business-owned Customer bounded context. Its `Domain` folder contains the aggregate and value objects inside the same service project. Keycloak owns authentication, credentials, MFA, sessions, federation, and token issuance. Customer service owns the commerce relationship: customer lifecycle, business contact data, saved addresses, and non-PII change audit records.

## Identity boundary

A Customer has an application-generated `Customer.Id` and a unique external identity link:

```text
IdentityProvider = keycloak
IdentitySubject  = validated access-token sub claim
```

The API never accepts identity-provider or subject values from a route or request body. `given_name`, `family_name`, and verified `email` claims initialize a newly provisioned Customer only. Subsequent business data is owned by Customer service. The current deployment accepts one Keycloak realm; before accepting multiple issuers, replace the logical provider discriminator with the validated OIDC issuer.

## Pure vertical slices

Each use case owns its HTTP endpoint, request contract, command or query, validation, handler, authorization policy, and response behavior:

```text
Features/Customers/
  Provisioning/V1/
    ProvisionCustomerEndpoint.cs
    ProvisionCustomerCommand.cs
    ProvisionCustomerValidator.cs
    ProvisionCustomerHandler.cs
    ProvisionCustomerResult.cs
  GettingCurrent/V1/
  UpdatingDetails/V1/
  AddingAddress/V1/
  UpdatingAddress/V1/
  RemovingAddress/V1/
  Exporting/V1/
  ClosingAccount/V1/
```

Every `V1` directory uses one top-level responsibility per source file. Operation-specific request, response, and result contracts remain in that versioned slice. Customer representations reused by multiple slices remain under `Features/Customers/Common`, with one top-level DTO or mapper per file.

Commands implement the shared `ICommand<TResponse>` contract and handlers implement `ICommandHandler<TCommand, TResponse>`. Read-only requests implement `IQuery<TResponse>` and use `IQueryHandler<TQuery, TResponse>`. MediatR remains the dispatcher, while the shared CQRS interfaces make command/query intent enforceable.

Only stable cross-slice primitives are shared: response mapping, ETag parsing, authorization constants, composable customer queries, audit persistence, address-default persistence, and PostgreSQL error classification. There is no shared application-service layer or repository facade that couples the slices. Architecture tests enforce the file manifest, prevent raw `IRequest`/`IRequestHandler` usage inside Customer slices, reject sibling-slice dependencies, and prevent reintroducing monolithic `*Slice.cs` files.

## Domain boundary

The domain model lives under `Customer.Api/Domain` and compiles into the Customer API assembly. This keeps the vertical-slice service as one project while preserving a clear source boundary. Architecture tests prevent domain files from referencing ASP.NET Core, MediatR, FluentValidation, EF Core, application features, persistence, or infrastructure.

## Error boundary

`Microservices.Primitives` provides framework-free `Result`, `Result<T>`, `OperationError`, and `ErrorCategory` contracts. Successful reference-type results cannot contain `null`, and successful values are created explicitly with `Result.Success(value)`.

The domain returns expected invariant outcomes such as invalid value objects, inactive-customer mutations, missing aggregate children, address limits, address identity conflicts, and stale aggregate versions. It does not return HTTP types and does not use aggregate-not-found, header, authentication, database, or idempotency-key terminology.

Application handlers own aggregate absence, authentication-claim extraction, request preconditions, known database conflicts, and idempotency behavior. An address identity conflict is translated into `customer.idempotency_key_reused` only when the application knows the address identity came from an API idempotency key.

Only the presentation layer maps semantic errors to HTTP Problem Details. Customer error types are resolvable under `/errors/v1/customer/{code}` and include a stable code, title, status, retry guidance, request instance, and trace identifier. `OperationError` contains only client-safe descriptions and explicitly approved public metadata; exception messages and diagnostic context are never copied automatically to responses.

Audit construction consumes aggregate IDs, validated identity subjects, application-owned action constants, and aggregate versions. Invalid values indicate a programming, configuration, or corrupt-state defect, so audit construction throws and follows the safe `500` path instead of returning a misleading client `400`.

`Microservices.ServiceDefaults` supplies the shared FluentValidation, status-code-page, content-negotiation fallback, trace-correlation, and safe unhandled-exception pipeline used by both Customer API and ServiceTemplate API. See [Error handling architecture](error-handling.md).

## Authorization and least privilege

Customer API validates the `customer-api` audience, exact authorized party, required token claims, and `customer-api` client roles. The required client role is `customer-user`.

Customer capabilities are optional Keycloak client scopes rather than default mobile-client grants:

- `customers.self.read`
- `customers.self.update`
- `customers.addresses.write`
- `customers.self.export`
- `customers.self.delete`

The mobile client requests only the Customer audience, role mapper, and capability scopes needed for the current operation. Order Scalar and Customer Scalar use separate public PKCE clients and separate exact redirect URIs.

## Provisioning

```http
PUT /api/v1/customers/me
Authorization: Bearer <access-token>
```

Provisioning is idempotent. A database unique constraint on `(IdentityProvider, IdentitySubject)` is the final concurrency guard. Concurrent first requests produce one Customer; the losing request reloads and returns it.

- `201 Created` when created.
- `200 OK` when already provisioned.
- Every successful response includes a strong `ETag` such as `"customer-4"`.

## Optimistic concurrency

Every state-changing operation except initial provisioning requires the latest strong ETag:

```http
If-Match: "customer-4"
```

- Missing ETag: `428 Precondition Required`.
- Invalid ETag: `400 Bad Request`.
- Stale ETag or concurrent database update: `412 Precondition Failed`.

Handlers compare the client version before mutation, and EF Core uses `Customer.Version` as the database concurrency token. This protects both overlapping transactions and delayed stale clients.

## Address idempotency and defaults

Address creation requires a stable GUID idempotency key:

```http
Idempotency-Key: 018f50a0-8f3c-7cf4-b4ef-8c09f8f02a1f
```

The key becomes the address identifier. Repeating the same request returns the existing result; reusing the key with different data returns `409 Conflict` with `customer.idempotency_key_reused`. This remains idempotent even when the retry carries the ETag used by the original request.

The aggregate enforces at most 20 saved addresses and one shipping/billing default. PostgreSQL filtered unique indexes duplicate the default invariants. When a default changes, handlers clear competing rows inside the same transaction before saving the aggregate, avoiding unique-index command-ordering races.

Country codes are a domain value object and must be exactly two ASCII letters. API validation is defense in depth; the domain remains authoritative.

## API

| Method | Route | Capability | Purpose |
| --- | --- | --- | --- |
| `PUT` | `/api/v1/customers/me` | `customers.self.update` | Idempotently provision current Customer |
| `GET` | `/api/v1/customers/me` | `customers.self.read` | Read current Customer and addresses |
| `PUT` | `/api/v1/customers/me/details` | `customers.self.update` | Replace business contact data |
| `POST` | `/api/v1/customers/me/addresses` | `customers.addresses.write` | Idempotently add an address |
| `PUT` | `/api/v1/customers/me/addresses/{addressId}` | `customers.addresses.write` | Replace an owned address |
| `DELETE` | `/api/v1/customers/me/addresses/{addressId}` | `customers.addresses.write` | Remove an owned address |
| `GET` | `/api/v1/customers/me/export` | `customers.self.export` | Export Customer-owned personal data |
| `DELETE` | `/api/v1/customers/me` | `customers.self.delete` | Anonymize PII, remove addresses, deactivate Customer |

Customer responses set `Cache-Control: no-store` and `Pragma: no-cache`.

## Lifecycle and PII

`Active` Customers may mutate data. `Suspended` is reserved for a future administrative slice. Account closure is permanent business deactivation:

- first name, last name, email, and phone are removed;
- saved addresses are deleted;
- status becomes `Deactivated`;
- the non-PII Customer ID and identity link remain to prevent accidental reprovisioning;
- Order records retain their own immutable historical address/contact snapshots.

Mutation audit entries are written in the same database transaction and contain action, actor subject, timestamp, and Customer version—never old or new PII values. Access logs and export-access auditing remain platform observability responsibilities.

Retention periods, backup encryption, restore testing, audit-log export, and legal-hold policy must be configured by the production platform and compliance owners. Database backups must be encrypted, access-controlled, restore-tested, and retained according to the approved data-retention schedule.

## Persistence and deployment

Customer service owns the `customer` PostgreSQL database and `CustomerDbContext`. API replicas never run migrations. Deploy and complete `Customer.Migrator` before starting or rolling API replicas.

```bash
dotnet ef migrations add <Name> \
  --project src/Services/Customer/Customer.Api \
  --startup-project src/Services/Customer/Customer.Api \
  --context CustomerDbContext \
  --output-dir Persistence/Migrations
```

Aspire creates the local database, completes the migrator, and then starts Customer API. Production must use deployment-specific authority, connection strings, secrets, host allow-lists, TLS termination, database credentials, and authorized-party configuration. Localhost settings are development defaults only.

## Verification

CI runs:

- restore and zero-warning Release builds;
- domain source-boundary tests;
- result null, metadata, and success/failure invariant tests;
- domain failure-atomicity and internal-audit-invariant tests;
- authenticated HTTP integration tests against PostgreSQL;
- customer and platform error-catalog tests;
- validation and framework-generated Problem Details tests;
- unsupported `Accept` and safe unknown-exception fallback tests;
- concurrent provisioning and idempotent address retries;
- ETag/precondition behavior;
- default-address persistence behavior;
- account anonymization and audit persistence;
- production Keycloak image build;
- live Keycloak realm import and least-privilege scope verification.

Payment methods and preferences remain outside this bounded context. Payment credentials belong in a separate payment boundary and must be represented only by payment-provider references.
