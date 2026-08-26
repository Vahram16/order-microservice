# Security and Identity Boundary

Security changes are high-risk by default. Preserve least privilege and the split between identity provider responsibilities and resource-API responsibilities.

## Keycloak owns

Keycloak owns:

- users and credentials;
- password policy/recovery;
- MFA/passkeys/federation;
- browser login/logout sessions;
- OAuth 2.0/OpenID Connect endpoints;
- access/refresh/ID token issuance;
- signing keys and identity administration.

The API must never receive a user's Keycloak password or exchange a native client's authorization code.

## Resource API owns

Each API owns validation and authorization for its resource boundary:

- issuer/audience/signature/lifetime/type validation;
- required claims;
- exact authorized-party (`azp`) policy;
- roles issued for the configured resource client;
- tenant/resource ownership and domain authorization;
- state-transition authorization where business rules require it.

Scopes/roles do not replace resource ownership or domain authorization.

## Trusted identity

Use the validated access-token subject/claims as the authoritative external identity. Customer's identity link is based on validated provider/subject, not request/route values.

Never accept a caller-supplied subject/provider as trusted identity when the token already establishes it.

## Customer least privilege

Customer uses distinct `backend-api` client roles for self-read, self-update,
address-write, export, and delete. Endpoints require the exact role for the operation.

Do not broaden default client grants or accept unrelated client roles to make a test/request pass.

## OAuth client safety

Development native/Scalar clients are public PKCE clients with exact redirect URIs and intentionally limited grants. Production configuration must replace development URIs/clients with environment-specific controlled settings.

Do not introduce:

- resource-owner password/direct grants;
- implicit flow;
- wildcard redirect URIs/origins;
- client secrets in public native clients;
- committed production/bootstrap credentials;
- unbounded authorized-party acceptance.

## Error safety

Authentication/authorization failures must not leak token contents, exception details, secrets, or policy internals. Preserve the platform's safe Problem Details behavior.

## Canonical evidence

- `docs/keycloak-integration.md`;
- `src/Shared/Microservices.Security/ApiSecurityExtensions.cs`;
- `ApiSecurityOptions.cs` / validator;
- `AccessTokenClaimsValidator.cs`;
- `KeycloakRoleClaimsMapper.cs`;
- `ScopeAuthorization.cs`;
- `SecurityClaimsPrincipalExtensions.cs`;
- `src/Services/Customer/Customer.Api/Infrastructure/CurrentIdentity.cs`;
- `Features/Customers/Common/CustomerAuthorization.cs`;
- Customer endpoints;
- `tests/Microservices.Security.Tests/`;
- `tests/Customer.Api.Tests/CurrentIdentityTests.cs`;
- `scripts/verify-keycloak-development.sh`.

## Review questions

1. Is identity derived only from validated token state?
2. Is audience/authorized-party validation preserved?
3. Are required claims still enforced?
4. Is authorization least-privilege (exact client role + domain ownership where needed)?
5. Does any new client grant increase blast radius unnecessarily?
6. Could secrets/token details/diagnostics leak through logs or responses?
7. Does the change affect production identity configuration or require manual rollout/reconciliation?
8. Are security tests and live Keycloak smoke checks updated where relevant?
