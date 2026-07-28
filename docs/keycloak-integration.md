# Keycloak integration

## Responsibility boundary

Keycloak owns authentication and identity lifecycle:

- credentials, password policy, account recovery, MFA, passkeys, and federation;
- interactive login/logout sessions;
- OAuth 2.0 and OpenID Connect protocol endpoints;
- access, refresh, and ID token issuance;
- realm/client administration and signing-key rotation.

The Order API is a resource server. It owns authorization enforcement:

- signature, issuer, audience, token-type, expiration, and not-before validation;
- capability checks using OAuth scopes;
- application role checks;
- tenant, ownership, state-transition, and other domain rules;
- `401` for missing/invalid tokens and `403` for insufficient authorization.

The API never accepts a Keycloak password and never calls the token endpoint on behalf of a mobile
user.

## Mobile flow

`order-mobile` is a public native client:

- Authorization Code Flow is enabled.
- PKCE is required with `S256`.
- Direct Access Grants/password grant is disabled.
- Implicit Flow and Service Accounts are disabled.
- No client secret is embedded in the mobile application.
- Redirect URIs are exact allow-list entries.

The development realm uses `com.example.order:/oauth2redirect`. Replace it with the exact
platform-owned application/universal-link URI before release.

The mobile application opens the system browser, sends an authorization request, receives the code
through the registered redirect URI, and exchanges the code plus PKCE verifier directly with
Keycloak. It sends only the access token to the Order API.

## API validation

Resource APIs call:

```csharp
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);
```

Required production configuration:

```json
{
  "Security": {
    "Authority": "https://identity.example.com/realms/order",
    "Audience": "order-api",
    "RoleClientId": "order-api",
    "MapRealmRoles": false,
    "NameClaimType": "preferred_username",
    "RequireHttpsMetadata": true,
    "ClockSkew": "00:00:30",
    "ValidTokenTypes": [ "JWT", "at+jwt" ]
  }
}
```

The JWT bearer handler uses OIDC discovery and JWKS. Inbound claim remapping is disabled. Keycloak
client roles are read only from `resource_access.<RoleClientId>.roles`; roles for other clients are
ignored. Realm roles are not trusted unless `MapRealmRoles` is explicitly enabled.

`Security:Authority` must be the exact externally visible realm issuer. Production must use HTTPS.
Do not point production APIs at an internal hostname when tokens contain a public issuer.

## Policies

Use capability scopes at endpoint boundaries:

```csharp
group.MapGet("/{id:guid}", GetOrder)
    .RequireAuthorization(ScopePolicy.For("orders.read"));

group.MapPost("/", CreateOrder)
    .RequireAuthorization(ScopePolicy.For("orders.create"));

group.MapPost("/{id:guid}/cancel", CancelOrder)
    .RequireAuthorization(ScopePolicy.For("orders.cancel"));
```

Use client roles only for organizational privilege:

```csharp
adminGroup.RequireAuthorization(RolePolicy.For("order-admin"));
```

Scopes and roles never replace resource authorization. A handler must still verify, for example,
that `order.CustomerId` matches `User.GetSubject()`, the tenant matches `User.GetTenantId()`, and the
current order state permits the requested transition.

Authentication is the fallback policy. Only intentionally public endpoints may call
`AllowAnonymous()`.

## Local development

Aspire runs Keycloak 26.7.0 in development mode at:

- issuer: `http://localhost:8080/realms/order`
- admin console: `http://localhost:8080/admin/master/console/`
- management health endpoint: `http://localhost:9000/health/ready`

Admin credentials are Aspire parameters. The password is a secret parameter and is not committed.

The realm import is copied from
`src/AppHost/Microservices.AppHost/Keycloak/order-realm.json`. Startup import is a development
bootstrap mechanism. Keycloak skips a realm that already exists in its persistent volume. Delete
the local `order-keycloak-data` volume when intentionally reapplying a changed development realm.

## Production Keycloak

`infrastructure/keycloak/Containerfile` builds an optimized, pinned Keycloak image. Production must
provide PostgreSQL, TLS/reverse-proxy configuration, a stable hostname, secret-managed database
credentials, restricted admin access, backups, monitoring, and tested key rotation.

Do not use `start-dev`, development H2 storage, wildcard redirect URIs, committed users/passwords, or
startup realm import as the production configuration-management strategy. Use the Keycloak Operator
or a controlled Admin API/GitOps reconciliation process, and review upgrade notes before changing
the pinned Keycloak version.
