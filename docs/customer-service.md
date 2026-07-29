# Customer service

`Customer.Api` is the business-owned customer bounded context. Keycloak remains the identity provider and owns credentials, authentication, MFA, sessions, and token issuance. The Customer service owns commerce profile data and saved addresses.

## Identity boundary

A customer has an application-generated `Customer.Id` and a unique external identity link:

```text
IdentityProvider = keycloak
IdentitySubject  = access-token sub claim
```

The API never accepts either value from a request body or route. It derives them only from a validated bearer access token. The Keycloak `given_name`, `family_name`, and verified `email` claims are used only to initialize a newly provisioned customer. Subsequent business profile changes are owned by Customer service.

The current deployment supports one configured Keycloak realm. If multiple issuers are accepted later, evolve the discriminator from the logical provider name to the validated OIDC issuer before enabling the additional issuer.

## Provisioning

Provisioning is explicit, idempotent, and safe under concurrent first requests:

```http
PUT /api/v1/customers/me
Authorization: Bearer <access-token>
```

The database unique constraint on `(IdentityProvider, IdentitySubject)` is the final concurrency guard. A racing request that loses the insert reloads and returns the already-created customer.

Responses:

- `201 Created` when the customer is created.
- `200 OK` when the customer already exists.

## Aggregate

`Customer` is the aggregate root. `CustomerAddress` is an owned child entity and cannot be accessed independently of the authenticated customer.

The aggregate enforces:

- only active customers can mutate business data;
- at most 20 saved addresses;
- at most one default shipping address;
- at most one default billing address;
- normalized field lengths and two-letter country-code shape;
- failure-atomic in-memory mutations;
- a monotonically non-decreasing `UpdatedAt` value;
- an incrementing aggregate `Version` used as an EF Core concurrency token.

PostgreSQL filtered unique indexes duplicate the default-address invariants as a final persistence guard.

## API

| Method | Route | Purpose |
| --- | --- | --- |
| `PUT` | `/api/v1/customers/me` | Idempotently provision the current customer |
| `GET` | `/api/v1/customers/me` | Read the current customer and saved addresses |
| `PUT` | `/api/v1/customers/me/details` | Replace business-owned customer details |
| `POST` | `/api/v1/customers/me/addresses` | Add a saved address |
| `PUT` | `/api/v1/customers/me/addresses/{addressId}` | Replace an owned saved address |
| `DELETE` | `/api/v1/customers/me/addresses/{addressId}` | Remove an owned saved address |

All customer routes require an authenticated token with the `order-user` client role. Resource ownership is still enforced independently by resolving the aggregate through the current token subject.

## Persistence and deployment

Customer service owns the `customer` PostgreSQL database and `CustomerDbContext`. API replicas never apply migrations. Deploy and complete `Customer.Migrator` before starting or rolling API replicas.

Create future migrations with:

```bash
dotnet ef migrations add <Name> \
  --project src/Services/Customer/Customer.Api \
  --startup-project src/Services/Customer/Customer.Api \
  --context CustomerDbContext \
  --output-dir Persistence/Migrations
```

The Aspire AppHost creates the local database, runs the migrator to completion, and then starts Customer API.

## Deliberate exclusions

This first bounded-context increment does not include payment methods, preferences, customer administration, integration events, or deletion/anonymization workflows. Payment credentials must not be added to this aggregate; a future payment boundary should store only provider token references. Integration events should be added together with an outbox when a real downstream consumer and contract exist.
