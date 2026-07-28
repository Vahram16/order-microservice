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

admin_token="$(curl --fail --silent --show-error \
  --request POST \
  --header 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'client_id=admin-cli' \
  --data-urlencode "username=${admin_username}" \
  --data-urlencode "password=${admin_password}" \
  --data-urlencode 'grant_type=password' \
  "${keycloak_url}/realms/master/protocol/openid-connect/token" \
  | jq --raw-output --exit-status '.access_token')"

admin_get() {
  curl --fail --silent --show-error \
    --header "Authorization: Bearer ${admin_token}" \
    "${keycloak_url}$1"
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
api_client="$(admin_get '/admin/realms/order/clients?clientId=order-api')"

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

assert_json \
  "API bearer-only client security" \
  'length == 1 and
   .[0].bearerOnly == true and
   .[0].standardFlowEnabled == false and
   .[0].implicitFlowEnabled == false and
   .[0].directAccessGrantsEnabled == false and
   .[0].serviceAccountsEnabled == false and
   .[0].fullScopeAllowed == false' \
  "${api_client}"

mobile_id="$(jq --raw-output --exit-status '.[0].id' <<<"${mobile_client}")"
api_id="$(jq --raw-output --exit-status '.[0].id' <<<"${api_client}")"
role_scope_mappings="$(admin_get "/admin/realms/order/clients/${mobile_id}/scope-mappings/clients/${api_id}")"
assert_json \
  "mobile API role scope mapping" \
  'map(.name) | sort == ["order-user"]' \
  "${role_scope_mappings}"

client_scopes="$(admin_get "/admin/realms/order/clients/${mobile_id}/default-client-scopes")"
assert_json \
  "mobile default client scopes" \
  'map(.name) | contains(["order-api-audience", "profile", "email", "roles"])' \
  "${client_scopes}"

optional_scopes="$(admin_get "/admin/realms/order/clients/${mobile_id}/optional-client-scopes")"
assert_json \
  "mobile optional client scopes" \
  'map(.name) | contains(["offline_access", "orders.read", "orders.create", "orders.cancel"])' \
  "${optional_scopes}"

echo "Keycloak development realm verification passed."
