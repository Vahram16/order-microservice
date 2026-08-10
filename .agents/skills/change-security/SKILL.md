---
name: change-security
description: Plan, implement, or review authentication, token validation, Keycloak client/realm, claims, scopes, roles, resource authorization, or identity-boundary changes. Security changes are high-risk and must preserve least privilege and safe disclosure.
---

# Change Security

Load:

- the scoped owner document selected by planning (`platform/shared-projects.md`, `platform/apphost.md`, `platform/infrastructure.md`, or a service document as applicable);
- `docs/agent-context/architecture/security.md`;
- `docs/keycloak-integration.md`;
- nearest security implementation/tests.

Load `api-and-errors.md` only when HTTP authentication/authorization responses or endpoint policies change. Detailed testing guidance is deferred to `$verify-dotnet-change`.

## Procedure

1. Identify whether responsibility belongs to Keycloak, shared token-validation infrastructure, the resource API, or domain authorization.
2. Preserve issuer/audience/signature/lifetime/type/required-claim validation and exact authorized-party policy.
3. Derive trusted identity only from validated token state.
4. Apply least-privilege role/scope requirements and preserve resource/domain ownership checks.
5. Keep secrets, tokens, exception details, and policy internals out of client responses/committed configuration.
6. Update affected tests and live Keycloak smoke verification when realm/client configuration changes; verify through `$verify-dotnet-change`.

## Prohibited shortcuts

Do not enable weaker OAuth flows, wildcard redirects/origins, broad client grants, arbitrary authorized parties, credentials inside the API, caller-supplied identity over validated token identity, or disabled security checks to make automation/tests pass.

If a security requirement or owner is ambiguous, mark the plan high-risk/blocked rather than choosing a permissive default.