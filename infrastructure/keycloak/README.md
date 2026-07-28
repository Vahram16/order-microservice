# Production Keycloak image

Build the optimized image:

```bash
docker build \
  --file infrastructure/keycloak/Containerfile \
  --tag order-keycloak:26.7.0 \
  .
```

Supply runtime configuration from the deployment platform and secret store. At minimum:

- `KC_DB_URL`
- `KC_DB_USERNAME`
- `KC_DB_PASSWORD`
- `KC_HOSTNAME`
- TLS configuration, or a correctly configured trusted reverse proxy
- initial bootstrap-admin credentials only for first-time provisioning

Expose the user-facing HTTPS endpoint and the management health/metrics endpoint separately. Do not
expose the management endpoint publicly.

The image intentionally does not import a realm. Production realm/client configuration must be
reconciled by the Keycloak Operator or a controlled Admin API/GitOps process.
