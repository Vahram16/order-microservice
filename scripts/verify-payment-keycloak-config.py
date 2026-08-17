#!/usr/bin/env python3
"""Validate Payment resource-server and client capabilities in the development realm import."""

from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REALM = ROOT / "src/AppHost/Microservices.AppHost/Keycloak/order-realm.json"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"[payment-keycloak] ERROR: {message}")


def main() -> int:
    realm = json.loads(REALM.read_text(encoding="utf-8"))
    clients = {client["clientId"]: client for client in realm["clients"]}
    scopes = {scope["name"]: scope for scope in realm["clientScopes"]}

    payment_api = clients.get("payment-api")
    require(payment_api is not None, "payment-api client is missing")
    require(payment_api.get("bearerOnly") is True, "payment-api must be bearer-only")
    require(payment_api.get("fullScopeAllowed") is False, "payment-api must disable full scope")

    roles = realm.get("roles", {}).get("client", {}).get("payment-api", [])
    require([role.get("name") for role in roles] == ["payment-user"], "payment-api role set must be payment-user only")

    for scope_name in (
        "payment-api-roles",
        "payment-api-audience",
        "payments.methods.read",
        "payments.methods.write",
    ):
        require(scope_name in scopes, f"{scope_name} scope is missing")

    role_mapper = scopes["payment-api-roles"].get("protocolMappers", [{}])[0]
    require(
        role_mapper.get("config", {}).get("usermodel.clientRoleMapping.clientId") == "payment-api",
        "payment role mapper must be limited to payment-api",
    )
    audience_mapper = scopes["payment-api-audience"].get("protocolMappers", [{}])[0]
    require(
        audience_mapper.get("config", {}).get("included.client.audience") == "payment-api",
        "payment audience mapper must target payment-api",
    )

    scalar = clients.get("payment-scalar-dev")
    require(scalar is not None, "payment-scalar-dev client is missing")
    require(scalar.get("publicClient") is True, "payment-scalar-dev must be public")
    require(scalar.get("attributes", {}).get("pkce.code.challenge.method") == "S256", "payment Scalar must require PKCE S256")
    require(scalar.get("redirectUris") == ["https://localhost:7070/scalar/v1"], "payment Scalar redirect URI is incorrect")
    required_scalar_scopes = {
        "payment-api-roles",
        "payment-api-audience",
        "payments.methods.read",
        "payments.methods.write",
    }
    require(required_scalar_scopes.issubset(set(scalar.get("optionalClientScopes", []))), "payment Scalar scopes are incomplete")

    mobile = clients["order-mobile"]
    require(required_scalar_scopes.issubset(set(mobile.get("optionalClientScopes", []))), "mobile Payment scopes must be explicit optional scopes")
    require(
        not required_scalar_scopes.intersection(set(mobile.get("defaultClientScopes", []))),
        "mobile Payment scopes must not be granted by default",
    )

    mappings = realm.get("clientScopeMappings", {}).get("payment-api", [])
    mapped = {(mapping.get("client"), tuple(mapping.get("roles", []))) for mapping in mappings}
    require(("order-mobile", ("payment-user",)) in mapped, "mobile payment-user mapping is missing")
    require(("payment-scalar-dev", ("payment-user",)) in mapped, "Payment Scalar payment-user mapping is missing")

    print("[payment-keycloak] OK: Payment audience, roles, scopes, PKCE client, and mappings are consistent.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
