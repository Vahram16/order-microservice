#!/usr/bin/env bash
set -euo pipefail

readonly container_name="order-keycloak-ci"
readonly keycloak_image="quay.io/keycloak/keycloak:26.7.0"
readonly keycloak_url="http://127.0.0.1:19080"
readonly management_url="http://127.0.0.1:19090"
readonly admin_username="ci-admin"
readonly admin_password="ci-only-keycloak-admin-password"
readonly realm_import="${PWD}/src/AppHost/Microservices.AppHost/Keycloak"

cleanup() {
  docker rm --force "${container_name}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

assert_json() {
  local description="$1"
  local filter="$2"
  local json="$3"

  if ! jq --exit-status "${filter}" <<<"${json}" >/dev/null; then
    echo "Keycloak verification failed: ${description}" >&2
    jq . <<<"${json}" >&2
    exit 1
  fi

  echo "Verified: ${description}"
}

cleanup

docker run --detach --rm \
  --name "${container_name}" \
  --publish 19080:8080 \
  --publish 19090:9000 \
  --env KC_BOOTSTRAP_ADMIN_USERNAME="${admin_username}" \
  --env KC_BOOTSTRAP_ADMIN_PASSWORD="${admin_password}" \
  --env KC_HEALTH_ENABLED=true \
  --env KC_METRICS_ENABLED=true \
  --env KC_HOSTNAME="${keycloak_url}" \
  --env KC_HOSTNAME_STRICT=true \
  --volume "${realm_import}:/opt/keycloak/data/import:ro" \
  "${keycloak_image}" \
  start-dev --import-realm >/dev/null

for attempt in $(seq 1 60); do
  if curl --fail --silent "${management_url}/health/ready" >/dev/null 2>&1; then
    break
  fi

  if [[ "${attempt}" -eq 60 ]]; then
    docker logs "${container_name}"
    echo "Keycloak did not become ready." >&2
    exit 1
  fi

  sleep 2
done

discovery="$(curl --fail --silent --show-error \
  "${keycloak_url}/realms/order/.well-known/openid-configuration")"
if ! jq --exit-status \
  --arg issuer "${keycloak_url}/realms/order" \
  '(.issuer == $issuer) and ((.code_challenge_methods_supported | index("S256")) != null)' \
  <<<"${discovery}" >/dev/null; then
  echo "Keycloak verification failed: OIDC discovery issuer or PKCE support" >&2
  jq . <<<"${discovery}" >&2
  exit 1
fi
echo "Verified: OIDC discovery issuer and PKCE S256 support"

docker exec "${container_name}" /opt/keycloak/bin/kcadm.sh \
  config credentials \
  --server http://localhost:8080 \
  --realm master \
  --user "${admin_username}" \
  --password "${admin_password}" >/dev/null

admin_get() {
  local resource="${1#/admin/}"

  docker exec "${container_name}" /opt/keycloak/bin/kcadm.sh \
    get "${resource}" \
    --server http://localhost:8080 \
    --realm master
}

realm_configuration="$(admin_get '/admin/realms/order')"
assert_json \
  "realm hardening" \
  '.defaultSignatureAlgorithm == "RS256" and
   .passwordPolicy == "length(12) and notUsername and notEmail and passwordHistory(5)" and
   .bruteForceProtected == true and
   .revokeRefreshToken == true and
   .refreshTokenMaxReuse == 0 and
   .eventsEnabled == true and
   .adminEventsEnabled == true and
   .adminEventsDetailsEnabled == true' \
  "${realm_configuration}"

mobile_client="$(admin_get '/admin/realms/order/clients?clientId=mobile-app')"
backend_api_client="$(admin_get '/admin/realms/order/clients?clientId=backend-api')"

assert_json \
  "mobile public-client security and exact redirect allow-list" \
  'length == 1 and
   .[0].publicClient == true and
   .[0].standardFlowEnabled == true and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false and
   .[0].attributes["pkce.code.challenge.method"] == "S256" and
   (.[0].redirectUris | sort == [
     "com.example.order:/oauth2redirect",
     "https://localhost:7050/scalar/v1",
     "https://localhost:7060/scalar/v1",
     "https://localhost:7070/scalar/v1"
   ]) and
   (.[0].webOrigins | sort == [
     "https://localhost:7050",
     "https://localhost:7060",
     "https://localhost:7070"
   ])' \
  "${mobile_client}"

assert_json \
  "shared backend bearer-only client security" \
  'length == 1 and
   .[0].bearerOnly == true and
   .[0].standardFlowEnabled == false and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false' \
  "${backend_api_client}"

mobile_id="$(jq --raw-output --exit-status '.[0].id' <<<"${mobile_client}")"
backend_api_id="$(jq --raw-output --exit-status '.[0].id' <<<"${backend_api_client}")"
backend_roles="$(admin_get "/admin/realms/order/clients/${backend_api_id}/roles")"
assert_json \
  "backend API client roles" \
  'map(.name) | sort == [
    "customers.addresses.write",
    "customers.self.delete",
    "customers.self.export",
    "customers.self.read",
    "customers.self.update",
    "payments.manage",
    "payments.read",
    "product.manage",
    "product.read"
  ]' \
  "${backend_roles}"

mobile_backend_roles="$(admin_get "/admin/realms/order/clients/${mobile_id}/scope-mappings/clients/${backend_api_id}")"
assert_json \
  "mobile client role-scope mapping" \
  'map(.name) | sort == [
    "customers.addresses.write",
    "customers.self.delete",
    "customers.self.export",
    "customers.self.read",
    "customers.self.update",
    "payments.manage",
    "payments.read",
    "product.manage",
    "product.read"
  ]' \
  "${mobile_backend_roles}"

customer_backend_roles="$(admin_get "/admin/realms/order/roles/customer/composites/clients/${backend_api_id}")"
assert_json \
  "customer realm-role permission bundle" \
  'map(.name) | sort == [
    "customers.addresses.write",
    "customers.self.read",
    "customers.self.update",
    "payments.manage",
    "payments.read",
    "product.read"
  ]' \
  "${customer_backend_roles}"

admin_backend_roles="$(admin_get "/admin/realms/order/roles/admin/composites/clients/${backend_api_id}")"
assert_json \
  "admin realm-role permission bundle" \
  'map(.name) | sort == [
    "customers.addresses.write",
    "customers.self.delete",
    "customers.self.export",
    "customers.self.read",
    "customers.self.update",
    "payments.manage",
    "payments.read",
    "product.manage",
    "product.read"
  ]' \
  "${admin_backend_roles}"

mobile_defaults="$(admin_get "/admin/realms/order/clients/${mobile_id}/default-client-scopes")"
assert_json \
  "mobile default identity, audience, and role mappers" \
  'map(.name) | contains([
    "basic", "profile", "email", "backend-api-audience", "backend-api-roles"
  ])' \
  "${mobile_defaults}"

all_client_scopes="$(admin_get '/admin/realms/order/client-scopes')"
backend_role_scope="$(jq --compact-output '[.[] | select(.name == "backend-api-roles")]' <<<"${all_client_scopes}")"
assert_json \
  "backend role protocol mapper" \
  '(length == 1) and
   .[0].protocolMappers[0].config["usermodel.clientRoleMapping.clientId"] == "backend-api" and
   .[0].protocolMappers[0].config["claim.name"] == "resource_access.${client_id}.roles" and
   .[0].protocolMappers[0].config["access.token.claim"] == "true" and
   .[0].protocolMappers[0].config["id.token.claim"] == "false"' \
  "${backend_role_scope}"

backend_audience_scope="$(jq --compact-output '[.[] | select(.name == "backend-api-audience")]' <<<"${all_client_scopes}")"
assert_json \
  "backend audience protocol mapper" \
  '(length == 1) and
   .[0].protocolMappers[0].config["included.client.audience"] == "backend-api" and
   .[0].protocolMappers[0].config["access.token.claim"] == "true"' \
  "${backend_audience_scope}"

echo "Keycloak development realm verification passed."
