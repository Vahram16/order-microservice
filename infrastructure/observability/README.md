# Messaging observability

This directory contains a small starting dashboard and alert set for the shared
MassTransit/RabbitMQ baseline.

## Metric sources

The assets combine:

1. Application OpenTelemetry metrics:
   - MassTransit's built-in transport and consumer instrumentation;
   - the `Microservices.Messaging` meter for retry, redelivery, consumer failure/duration, and
     PostgreSQL outbox backlog/age.
2. RabbitMQ Prometheus metrics exposed by the repository's RabbitMQ image.

The telemetry pipeline should preserve `service.name` as `service_name`. Standard Prometheus name
translation converts dots in instruments and attributes to underscores. Verify the translated names
against the deployed collector before rollout.

## Prometheus

Merge `prometheus/rabbitmq-scrape.yml` into the environment configuration and load
`prometheus/messaging-alerts.yml` as a rule file.

The checked-in rules intentionally cover only broadly actionable conditions:

- sustained consumer retries;
- non-empty `_error` and `_skipped` queues;
- outbox backlog and age;
- outbox metric-collection failures;
- RabbitMQ scrape failure and connection churn.

Thresholds are initial defaults, not universal SLOs. Each production service should tune or extend
them only after representative traffic and ownership are known.

## Grafana

Import `grafana/messaging-reliability-dashboard.json` and bind `DS_PROMETHEUS` to the environment
Prometheus datasource. The dashboard focuses on retry/redelivery rate, consumer failures,
error/skipped queue depth, outbox backlog/age, and RabbitMQ connection churn.

Avoid adding identifiers, payload values, exception messages, URLs, customer IDs, order IDs, or
arbitrary headers as metric labels.

## Health semantics

Metrics collection is observability, not a service-readiness dependency. A collector failure should
produce a warning and collection-failure metric, but it must not remove an otherwise functioning API
or consumer from traffic.

## Validation

CI validates dashboard JSON, Prometheus rules, and scrape configuration. Environment rollout must
also query the expressions against real labels because syntax validation alone cannot prove that a
collector exports every expected metric name.
