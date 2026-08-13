# Infrastructure Context

Use this document only for changes under `infrastructure/` or when application work explicitly changes broker, identity-provider, or observability deployment assets.

## `infrastructure/keycloak`

Owns production Keycloak image/build assets and identity-provider deployment concerns.

Do not confuse this with resource-API authorization. Keycloak owns identity-provider responsibilities; APIs own token validation plus resource/domain authorization.

Security-sensitive changes require `../architecture/security.md`, `docs/keycloak-integration.md`, relevant security tests, and explicit human review.

Do not introduce development-only identity configuration into production deployment strategy.

## `infrastructure/rabbitmq`

Owns the pinned RabbitMQ image/plugin baseline used by development/CI/production-oriented verification.

Changes may alter broker capabilities, retry/redelivery support, topology, metrics, or operational behavior. Load `../architecture/messaging.md` and relevant messaging reliability tests before modifying these assets.

Do not change broker/runtime policy to compensate for an application design problem without an explicit architecture decision.

## `infrastructure/observability`

Owns deployable Grafana/Prometheus messaging observability assets.

Keep dashboards, scrape configuration, rules, metric names, labels, and alert semantics aligned with the actual implementation/tests. Observability assets should report behavior; they should not become a source of business logic.

## Boundary rule

Infrastructure directories own deployable operational assets, not service application/domain source.

A feature change should modify infrastructure only when approved acceptance criteria genuinely require a deployment/runtime capability change. New production secrets, destructive operations, security weakening, and unbounded infrastructure changes remain manual/high-risk work.