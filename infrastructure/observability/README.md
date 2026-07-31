# Messaging observability deployment

This directory contains the deployable monitoring artifacts for the shared MassTransit/RabbitMQ
failure-delivery policy.

## Inputs

The dashboard and alerts combine two metric sources:

1. Application OpenTelemetry metrics exported by each service:
   - the MassTransit meter;
   - the `Microservices.Messaging` meter.
2. RabbitMQ Prometheus metrics exposed on port `15692` by the repository's RabbitMQ image.

The OpenTelemetry Collector or Prometheus receiver must preserve the `service.name` resource
attribute as the Prometheus `service_name` label. Standard Prometheus name translation converts
OpenTelemetry instrument and attribute dots to underscores, for example:

- `messaging.consumer.retry.attempts` → `messaging_consumer_retry_attempts_total`;
- `messaging.consumer.attempt.duration` → `messaging_consumer_attempt_duration_seconds`;
- `messaging.destination.name` → `messaging_destination_name`;
- `db.context` → `db_context`.

Validate those translated names against the environment's collector version before rollout. When a
collector uses a different resource-label strategy, adapt the ingestion pipeline or add recording
rules; do not maintain divergent dashboard copies per service.

## Prometheus

Merge `prometheus/rabbitmq-scrape.yml` into the environment Prometheus configuration. The aggregate
RabbitMQ endpoint supplies broker and connection metrics. The detailed endpoint is restricted to
`queue_coarse_metrics` and `queue_metrics`, which supply queue depth and head-message timestamp.

Detailed per-object metrics can create high cardinality. Do not add exchange, channel, connection,
consumer, or Erlang-process metric families without a capacity and retention review.

Load `prometheus/messaging-alerts.yml` as a Prometheus rule file. Route:

- `owner=service-owner` by the endpoint/service ownership catalogue;
- `owner=platform` to the RabbitMQ platform team.

The checked-in thresholds are safe starting values, not universal SLOs. Capacity tests and production
traffic should drive reviewed threshold changes. Alert changes require the same review as application
reliability changes.

## Grafana

Import `grafana/messaging-reliability-dashboard.json` and bind the `DS_PROMETHEUS` variable to the
environment Prometheus datasource. The dashboard contains panels for:

- retry and delayed-redelivery attempts;
- consumer failures and p95 attempt latency;
- `_error` and `_skipped` queue depth;
- oldest queued-message age;
- PostgreSQL outbox backlog and oldest delivery age;
- RabbitMQ scrape health and connection churn.

Dashboard labels intentionally exclude payload values, headers, identities, tokens, and domain data.
Do not add high-cardinality MessageId or CorrelationId labels to metrics; those identifiers belong in
traces and structured logs.

## Validation

CI validates:

- Grafana dashboard JSON syntax;
- Prometheus alert-rule syntax and PromQL through `promtool check rules`;
- RabbitMQ scrape configuration through `promtool check config`.

A successful syntax check does not prove that every metric is present in an environment. Deployment
verification must also query each alert and dashboard expression after the first application and
RabbitMQ scrape, and confirm that expected labels are populated.
