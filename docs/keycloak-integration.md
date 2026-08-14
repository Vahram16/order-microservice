# Keycloak integration

## Scope

This repository contains shared security infrastructure, Customer and Product resource APIs, and
`ServiceTemplate.Api`. It does not contain an Order domain or real Order endpoints. Names such as
`order-api`, `order-mobile`, and `orders.read` define the intended identity contract; they do not
imply that Order domain authorization has already been implemented.

## Responsibility boundary

Keycloak owns authentication and identity lifecycle:

- credentials, password policy, account recovery, MFA, passkeys, and federation;
- interactive login/logout sessions;
- OAuth 2.0 and OpenID Connect protocol endpoints;
- access, refresh, and ID token issuance;
- realm/client administration and signing-key rotation.

A resource API owns authorization enforcement:

- signature, issuer, audience, token-type, expiration, and not-before validation;
- required access-token claim validation;
- authorized-party (`azp`) allow-list validation;
- capability checks using OAuth scopes;
- application-role checks;
- tenant, ownership, state-transition, and other domain rules once those concepts exist;
- `401` for missing/invalid tokens and `403` for insufficient authorization.

An API never accepts a Keycloak password and never calls the token endpoint on behalf of a native
user.

## Mobile flow

`order-mobile` is a public native client:

- Authorization Code Flow is enabled;
- PKCE is required with `S256`;
- Direct Access Grants/password grant is disabled;
- Implicit Flow and Service Accounts are disabled;
- no client secret is embedded in the mobile application;
- Full Scope Allowed is disabled;
- redirect URIs are exact allow-list entries.

The development realm uses `com.example.order:/oauth2redirect`. Replace it with the exact
application-owned Android App Link, iOS Universal Link, or private-use URI before release.

The mobile application opens the system browser, sends an authorization request, receives the code
through the registered redirect URI, and exchanges the code plus PKCE verifier directly with
Keycloak. It sends only the access token to the API.

## Local Scalar clients

The development realm includes a separate `scalar-dev` public client for the HTTPS API-documentation
endpoint at `https://localhost:7040/scalar/v1`. It uses Authorization Code with PKCE `S256`, exact
redirect URI and web-origin allow-lists, and the same minimal Order API audience, application
scopes, and `order-user` role scope mapping as the mobile client. It does not request
`offline_access`; interactive API documentation does not need an offline refresh token. It is a
development-only client; do not add it to a production API's authorized-party allow-list.

Both token-issuing clients receive the realm's `basic` default client scope. Its Keycloak
`oidc-sub-mapper` adds the stable OIDC `sub` claim required by API token validation. This scope is
declared explicitly because imported realms do not inherit client scopes from Keycloak's realm
defaults.

The realm defines `order-user`, `order-manager`, and `order-admin` as `order-api` client roles. The
mobile client's explicit role scope mapping contains only `order-user`, so privileged manager/admin
roles are not emitted through the public mobile client. A dedicated `order-api-roles` client scope
uses Keycloak's client-specific role mapper and writes only resolved `order-api` roles to
`resource_access.order-api.roles` in access tokens.

Customer and Product use separate development Scalar clients at ports `7050` and `7060`. The
`product-scalar-dev` client has exact redirect/origin allow-lists, PKCE `S256`, and only the
`product-api-audience` plus basic identity scopes. Product capability scopes and roles remain
undefined until an explicit authorization requirement is approved.

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
    "ValidAuthorizedParties": [ "order-mobile" ],
    "RequiredClaims": [ "sub", "iat", "jti" ],
    "MapRealmRoles": false,
    "NameClaimType": "preferred_username",
    "RequireHttpsMetadata": true,
    "ClockSkew": "00:00:30",
    "ValidTokenTypes": [ "JWT", "at+jwt" ]
  }
}
```

The JWT bearer handler uses OIDC discovery and JWKS. Inbound claim remapping is disabled. It validates
issuer, audience, signature, signing key, lifetime, and JWT type. After cryptographic validation, the
application requires each configured claim and requires exactly one non-empty `azp` whose value is in
`ValidAuthorizedParties`.

The `azp` check matters because audience and authorized party represent different boundaries. A
token is accepted only when it is intended for this API and was issued to an approved calling client.
Add future trusted web, worker, or administration clients explicitly rather than weakening the check.

Keycloak client roles are read only from `resource_access.<RoleClientId>.roles`; roles for other
clients are ignored. Realm roles are not trusted unless `MapRealmRoles` is explicitly enabled.

`Security:Authority` must be the exact externally visible realm issuer. Production must use HTTPS. Do
not point production APIs at an internal hostname when tokens contain a public issuer.

## Policies

Use capability scopes at future endpoint boundaries:

```csharp
endpoint.RequireAuthorization(ScopePolicy.For("orders.read"));
```

Use client roles only for organizational privilege:

```csharp
adminGroup.RequireAuthorization(RolePolicy.For("order-admin"));
```

These snippets describe how a future domain service consumes the shared infrastructure. They are not
existing Order endpoints. Scopes and roles never replace resource authorization; a real handler must
apply its actual subject, tenant, ownership, and state-transition rules.

Authentication is the fallback policy. Only intentionally public endpoints may call
`AllowAnonymous()`.

## Local development with Aspire

The AppHost uses the official Aspire Keycloak hosting integration. Run:

```bash
dotnet tool restore
dotnet restore
dotnet run --project src/AppHost/Microservices.AppHost
```

Aspire runs pinned Keycloak 26.7.0 in development mode and imports:

```text
src/AppHost/Microservices.AppHost/Keycloak/order-realm.json
```

Development endpoints:

```text
Issuer:        http://localhost:8080/realms/order
Admin console: http://localhost:8080/admin/master/console/
```

The default admin username is `admin`. Aspire generates a random password on the first run and stores
it in the AppHost user-secrets store. Inspect the generated development values with:

```bash
dotnet user-secrets list --project src/AppHost/Microservices.AppHost
```

The management endpoint is not assigned a fixed public host port. Inspect its URL and readiness state
through the Aspire dashboard.

Keycloak stores local realm state in the dedicated `keycloak` database on the Aspire-managed
PostgreSQL server; application data uses a separate database. Keycloak startup import creates a
realm only when it does not already exist, so it is not a reconciliation mechanism. When
intentionally reapplying a changed import, delete the `order` realm through the admin console and
restart Aspire.

The `Aspire.Hosting.Keycloak` package is AppHost-only and development-only. The production Keycloak
image and deployment do not depend on Aspire.

## Automated verification

The CI pipeline builds the optimized Keycloak Containerfile and runs
`scripts/verify-keycloak-development.sh`. The script starts the actual pinned Keycloak image, imports
the realm, checks OIDC discovery, and queries the Admin API to verify:

- PKCE `S256` support;
- password, refresh-token, brute-force, and audit settings;
- public-client and bearer-only boundaries;
- Direct Access Grants, Implicit Flow, and Service Accounts disabled;
- Full Scope Allowed disabled;
- the exact development redirect URI;
- explicit profile, email, audience, role, offline, and application scopes;
- only `order-user` in the mobile client's `order-api` role scope mapping;
- the `scalar-dev` public-client, redirect, origin, scopes, and `order-user` role boundary;
- the isolated Product bearer-only resource client, audience mapper, and PKCE Scalar client without
  invented capability grants;
- the client-specific `order-api-roles` protocol mapper and its access-token-only claim target.

Unit tests separately verify JWT option hardening, exact scopes, role mapping, required claims, and the
`azp` allow list.

## Production Keycloak

`infrastructure/keycloak/Containerfile` builds an optimized, pinned Keycloak image. It is a runtime
image definition, not a complete production platform.

Production must provide:

- external PostgreSQL, connection pooling appropriate to the topology, backups, and tested restore;
- TLS or a correctly configured trusted reverse proxy and a stable public issuer;
- secret-managed database and first-time bootstrap credentials;
- restricted admin console and non-public management endpoints;
- monitoring, alerting, resource limits, availability, and upgrade procedures;
- controlled realm/client reconciliation;
- exact production redirect URIs, SMTP, onboarding, MFA/step-up, and account-recovery policies;
- signing-key rotation and emergency-access procedures.

Do not use `start-dev`, development H2 storage, wildcard redirect URIs, committed users/passwords, or
startup realm import as the production configuration-management strategy. Use the Keycloak Operator
or a controlled Admin API/GitOps reconciliation process, and review upgrade notes before changing the
pinned Keycloak version.
