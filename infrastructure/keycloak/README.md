# Production Keycloak image

This folder defines the optimized Keycloak runtime image. It is separate from the Aspire AppHost:

- `src/AppHost/Microservices.AppHost` runs Keycloak for local development;
- `infrastructure/keycloak/Containerfile` builds the production runtime image;
- neither this image nor a production deployment depends on Aspire.

Build the pinned image:

```bash
docker build \
  --file infrastructure/keycloak/Containerfile \
  --tag order-keycloak:26.7.0 \
  .
```

The CI workflow builds this Containerfile on every pull request. Image publication, vulnerability
scanning, SBOM/provenance generation, signing, and deployment belong in the target environment's
release pipeline.

Supply runtime configuration from the deployment platform and secret store. At minimum:

- `KC_DB_URL`;
- `KC_DB_USERNAME`;
- `KC_DB_PASSWORD`;
- `KC_HOSTNAME`;
- TLS configuration, or a correctly configured trusted reverse proxy;
- initial bootstrap-admin credentials only for first-time provisioning.

A production deployment must also define replica topology, resources, availability, network policy,
trusted proxy addresses, database backups/restores, monitoring, alerting, upgrades, and emergency
administration.

Expose the user-facing HTTPS endpoint and the management health/metrics endpoint separately. Do not
expose the management endpoint publicly.

The image intentionally does not import a realm. Production realm/client configuration must be
reconciled by the Keycloak Operator or a controlled Admin API/GitOps process. The development import
under `src/AppHost` contains local-only hostname and redirect settings and must not be used as the
production configuration source without environment-specific reconciliation.
