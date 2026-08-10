---
name: change-security
description: Plan, implement, or review authentication, token validation, Keycloak client/realm, claims, scopes, roles, resource authorization, or identity-boundary changes. Security changes are high-risk and must preserve least privilege and safe disclosure.
---

# Change Security

Load:

- `docs/agent-context/architecture/security.md`;
- `docs/agent-context/architecture/testing.md`;
- `docs/keycloak-integration.md`;
- nearest security implementation/tests.

Load `api-and-errors.md` when HTTP authentication/authorization responses or endpoint policies change.

## Procedure

1. Identify whether the responsibility belongs to Keycloak, shared token-validation infrastructure, the resource API, or domain authorization.
2. Preserve issuer/audience/signature/lifetime/type/required-claim validation and exact authorized-party policy.
3. Derive trusted identity only from validated token state.
4. Apply least-privilege role/scope requirements and preserve resource/domain ownership checks.
5. Keep secrets, tokens, exception details, and policy internals out of client responses and committed configuration.
6. Update unit/integration tests and live Keycloak smoke verification when realm/client configuration changes.

## Prohibited shortcuts

Do not enable weaker OAuth flows, wildcard redirects/origins, broad client grants, accept arbitrary authorized parties, move credentials into the API, trust caller-supplied identity over token identity, or disable a security check merely to make automation/tests pass.

If a security requirement is ambiguous, mark the plan high-risk/blocked rather than choosing a permissive default.