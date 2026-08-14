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

mobile_client="$(admin_get '/admin/realms/order/clients?clientId=order-mobile')"
order_api_client="$(admin_get '/admin/realms/order/clients?clientId=order-api')"
customer_api_client="$(admin_get '/admin/realms/order/clients?clientId=customer-api')"
product_api_client="$(admin_get '/admin/realms/order/clients?clientId=product-api')"
scalar_client="$(admin_get '/admin/realms/order/clients?clientId=scalar-dev')"
customer_scalar_client="$(admin_get '/admin/realms/order/clients?clientId=customer-scalar-dev')"
product_scalar_client="$(admin_get '/admin/realms/order/clients?clientId=product-scalar-dev')"

assert_json \
  "mobile public-client security" \
  'length == 1 and
   .[0].publicClient == true and
   .[0].standardFlowEnabled == true and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false and
   .[0].attributes["pkce.code.challenge.method"] == "S256" and
   .[0].redirectUris == ["com.example.order:/oauth2redirect"]' \
  "${mobile_client}"

for client in "${order_api_client}" "${customer_api_client}" "${product_api_client}"; do
  assert_json \
    "API bearer-only client security" \
    'length == 1 and
     .[0].bearerOnly == true and
     .[0].standardFlowEnabled == false and
     .[0].implicitFlowEnabled == false and
     .[0].directAccessGrantsEnabled == false and
     .[0].serviceAccountsEnabled == false and
     .[0].fullScopeAllowed == false' \
    "${client}"
done

assert_json \
  "Product Scalar public-client security" \
  'length == 1 and
   .[0].publicClient == true and
   .[0].standardFlowEnabled == true and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false and
   .[0].attributes["pkce.code.challenge.method"] == "S256" and
   .[0].redirectUris == ["https://localhost:7060/scalar/v1"] and
   .[0].webOrigins == ["https://localhost:7060"]' \
  "${product_scalar_client}"

assert_json \
  "Order Scalar public-client security" \
  'length == 1 and
   .[0].publicClient == true and
   .[0].standardFlowEnabled == true and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false and
   .[0].attributes["pkce.code.challenge.method"] == "S256" and
   .[0].redirectUris == ["https://localhost:7040/scalar/v1"] and
   .[0].webOrigins == ["https://localhost:7040"]' \
  "${scalar_client}"

assert_json \
  "Customer Scalar public-client security" \
  'length == 1 and
   .[0].publicClient == true and
   .[0].standardFlowEnabled == true and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false and
   .[0].attributes["pkce.code.challenge.method"] == "S256" and
   .[0].redirectUris == ["https://localhost:7050/scalar/v1"] and
   .[0].webOrigins == ["https://localhost:7050"]' \
  "${customer_scalar_client}"

mobile_id="$(jq --raw-output --exit-status '.[0].id' <<<"${mobile_client}")"
order_api_id="$(jq --raw-output --exit-status '.[0].id' <<<"${order_api_client}")"
customer_api_id="$(jq --raw-output --exit-status '.[0].id' <<<"${customer_api_client}")"
product_api_id="$(jq --raw-output --exit-status '.[0].id' <<<"${product_api_client}")"
scalar_id="$(jq --raw-output --exit-status '.[0].id' <<<"${scalar_client}")"
customer_scalar_id="$(jq --raw-output --exit-status '.[0].id' <<<"${customer_scalar_client}")"
product_scalar_id="$(jq --raw-output --exit-status '.[0].id' <<<"${product_scalar_client}")"

mobile_order_roles="$(admin_get "/admin/realms/order/clients/${mobile_id}/scope-mappings/clients/${order_api_id}")"
assert_json "mobile Order API role scope mapping" 'map(.name) | sort == ["order-user"]' "${mobile_order_roles}"
scalar_order_roles="$(admin_get "/admin/realms/order/clients/${scalar_id}/scope-mappings/clients/${order_api_id}")"
assert_json "Order Scalar role scope mapping" 'map(.name) | sort == ["order-user"]' "${scalar_order_roles}"
mobile_customer_roles="$(admin_get "/admin/realms/order/clients/${mobile_id}/scope-mappings/clients/${customer_api_id}")"
assert_json "mobile Customer API role scope mapping" 'map(.name) | sort == ["customer-user"]' "${mobile_customer_roles}"
customer_scalar_roles="$(admin_get "/admin/realms/order/clients/${customer_scalar_id}/scope-mappings/clients/${customer_api_id}")"
assert_json "Customer Scalar role scope mapping" 'map(.name) | sort == ["customer-user"]' "${customer_scalar_roles}"
product_api_roles="$(admin_get "/admin/realms/order/clients/${product_api_id}/roles")"
assert_json "Product API has no invented client roles" 'length == 0' "${product_api_roles}"

mobile_defaults="$(admin_get "/admin/realms/order/clients/${mobile_id}/default-client-scopes")"
assert_json \
  "mobile default scopes exclude Customer API" \
  '(map(.name) | contains(["basic", "order-api-audience", "order-api-roles", "profile", "email"])) and
   (map(.name) | index("customer-api-audience") == null) and
   (map(.name) | index("customer-api-roles") == null)' \
  "${mobile_defaults}"

mobile_optional="$(admin_get "/admin/realms/order/clients/${mobile_id}/optional-client-scopes")"
assert_json \
  "mobile optional Customer capabilities" \
  'map(.name) | contains([
    "offline_access", "orders.read", "orders.create", "orders.cancel",
    "customer-api-audience", "customer-api-roles", "customers.self.read",
    "customers.self.update", "customers.addresses.write", "customers.self.export",
    "customers.self.delete"
  ])' \
  "${mobile_optional}"

scalar_defaults="$(admin_get "/admin/realms/order/clients/${scalar_id}/default-client-scopes")"
assert_json \
  "Order Scalar default scopes" \
  'map(.name) | contains(["basic", "order-api-audience", "order-api-roles", "profile", "email"])' \
  "${scalar_defaults}"
scalar_optional="$(admin_get "/admin/realms/order/clients/${scalar_id}/optional-client-scopes")"
assert_json \
  "Order Scalar optional scopes exclude Customer API" \
  '(map(.name) | contains(["orders.read", "orders.create", "orders.cancel"])) and
   (map(.name) | index("customer-api-audience") == null) and
   (map(.name) | index("offline_access") == null)' \
  "${scalar_optional}"

customer_scalar_defaults="$(admin_get "/admin/realms/order/clients/${customer_scalar_id}/default-client-scopes")"
assert_json \
  "Customer Scalar identity-only defaults" \
  '(map(.name) | contains(["basic", "profile", "email"])) and
   (map(.name) | index("customer-api-audience") == null) and
   (map(.name) | index("order-api-audience") == null)' \
  "${customer_scalar_defaults}"
customer_scalar_optional="$(admin_get "/admin/realms/order/clients/${customer_scalar_id}/optional-client-scopes")"
assert_json \
  "Customer Scalar optional capabilities" \
  'map(.name) | contains([
    "customer-api-audience", "customer-api-roles", "customers.self.read",
    "customers.self.update", "customers.addresses.write", "customers.self.export",
    "customers.self.delete"
  ])' \
  "${customer_scalar_optional}"

product_scalar_defaults="$(admin_get "/admin/realms/order/clients/${product_scalar_id}/default-client-scopes")"
assert_json \
  "Product Scalar audience-only defaults" \
  'map(.name) | sort == ["basic", "email", "product-api-audience", "profile"]' \
  "${product_scalar_defaults}"
product_scalar_optional="$(admin_get "/admin/realms/order/clients/${product_scalar_id}/optional-client-scopes")"
assert_json \
  "Product Scalar has no invented capability scopes" \
  'length == 0' \
  "${product_scalar_optional}"

all_client_scopes="$(admin_get '/admin/realms/order/client-scopes')"
basic_scope="$(jq --compact-output '[.[] | select(.name == "basic")]' <<<"${all_client_scopes}")"
assert_json \
  "OIDC basic subject protocol mapper" \
  '(length == 1) and
   ([.[0].protocolMappers[] |
      select(.protocolMapper == "oidc-sub-mapper" and
             .config["access.token.claim"] == "true" and
             .config["introspection.token.claim"] == "true")] | length == 1)' \
  "${basic_scope}"

order_role_scope="$(jq --compact-output '[.[] | select(.name == "order-api-roles")]' <<<"${all_client_scopes}")"
assert_json \
  "Order API role protocol mapper" \
  '(length == 1) and
   .[0].protocolMappers[0].config["usermodel.clientRoleMapping.clientId"] == "order-api" and
   .[0].protocolMappers[0].config["access.token.claim"] == "true" and
   .[0].protocolMappers[0].config["id.token.claim"] == "false"' \
  "${order_role_scope}"

customer_role_scope="$(jq --compact-output '[.[] | select(.name == "customer-api-roles")]' <<<"${all_client_scopes}")"
assert_json \
  "Customer API role protocol mapper" \
  '(length == 1) and
   .[0].protocolMappers[0].config["usermodel.clientRoleMapping.clientId"] == "customer-api" and
   .[0].protocolMappers[0].config["access.token.claim"] == "true" and
   .[0].protocolMappers[0].config["id.token.claim"] == "false"' \
  "${customer_role_scope}"

customer_audience_scope="$(jq --compact-output '[.[] | select(.name == "customer-api-audience")]' <<<"${all_client_scopes}")"
assert_json \
  "Customer API audience protocol mapper" \
  '(length == 1) and
   .[0].protocolMappers[0].config["included.client.audience"] == "customer-api" and
   .[0].protocolMappers[0].config["access.token.claim"] == "true"' \
  "${customer_audience_scope}"

product_audience_scope="$(jq --compact-output '[.[] | select(.name == "product-api-audience")]' <<<"${all_client_scopes}")"
assert_json \
  "Product API audience protocol mapper" \
  '(length == 1) and
   .[0].protocolMappers[0].config["included.client.audience"] == "product-api" and
   .[0].protocolMappers[0].config["access.token.claim"] == "true"' \
  "${product_audience_scope}"

echo "Keycloak development realm verification passed."
