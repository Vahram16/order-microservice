# Messaging observability deployment

This directory contains the deployable monitoring artifacts for the enforced
MassTransit/RabbitMQ reliability policy.

## Inputs

The dashboard and alerts combine two metric sources:

1. Application OpenTelemetry metrics exported by each service:
   - MassTransit's built-in meter for receive, consume, fault, and transport behavior;
   - the `Microservices.Messaging` meter for the bounded custom signals described below.
2. RabbitMQ Prometheus metrics exposed on port `15692` by the repository's RabbitMQ image.

The OpenTelemetry Collector or Prometheus receiver must preserve the `service.name` resource
attribute as the Prometheus `service_name` label. Standard Prometheus name translation converts
OpenTelemetry instrument and attribute dots to underscores, for example:

- `messaging.consumer.retry.attempts` → `messaging_consumer_retry_attempts_total`;
- `messaging.consumer.redelivery.deliveries` →
  `messaging_consumer_redelivery_deliveries_total`;
- `messaging.consumer.attempt.failures` →
  `messaging_consumer_attempt_failures_total`;
- `messaging.consumer.attempt.duration` →
  `messaging_consumer_attempt_duration_seconds`;
- `messaging.outbox.collector.healthy` → `messaging_outbox_collector_healthy`;
- `messaging.destination.name` → `messaging_destination_name`;
- `messaging.failure.disposition` → `messaging_failure_disposition`;
- `db.context` → `db_context`;
- `outbox.role` → `outbox_role`.

Validate translated names against the environment's collector version before rollout. When a
collector uses a different resource-label strategy, adapt the ingestion pipeline or add recording
rules; do not maintain divergent dashboard copies per service.

## Metric meanings

The custom metrics deliberately do not duplicate MassTransit's complete instrumentation:

- `messaging.consumer.retry.attempts` counts one immediate retry invocation. Immediate retries
  inside a delayed delivery are included.
- `messaging.consumer.redelivery.deliveries` counts one broker-backed delayed delivery before that
  delivery's immediate retry sequence. It is not another immediate retry.
- `messaging.consumer.attempt.failures` counts one consumer invocation that threw. A later retry or
  redelivery may still succeed.
- `messaging.consumer.attempt.duration` measures one invocation and excludes retry/redelivery wait.
- `messaging.outbox.backlog` and `messaging.outbox.oldest.age` report pending `OutboxMessage` rows by
  bounded `bus` or `consumer` role; delivered cleanup state is excluded.
- collector health, last-success age, and failure counters indicate whether backlog values are
  current. A database failure leaves last-known values unchanged instead of emitting false zeroes.

RabbitMQ `_error` queue depth is the terminal message-failure signal. `_skipped` depth is a distinct
routing or contract-deployment signal. Dashboards and alerts never infer terminal failure from a
failed consumer invocation alone.

## Cardinality rules

Application metric dimensions are limited to stable service name, stable endpoint name, contract
type, exception type, failure disposition, DbContext type, and outbox role.

Do not add message IDs, correlation IDs, causation IDs, exception messages, URLs, routing addresses,
customer IDs, order IDs, payload values, arbitrary headers, tokens, or other domain data as metric
labels. Those values belong only in access-controlled traces and structured logs.

## Prometheus

Merge `prometheus/rabbitmq-scrape.yml` into the environment Prometheus configuration. The aggregate
RabbitMQ endpoint supplies broker and connection metrics. The detailed endpoint is restricted to
`queue_coarse_metrics` and `queue_metrics`, which supply queue depth and head-message timestamp.

Detailed per-object metrics can create high cardinality. Do not add exchange, channel, connection,
consumer, or Erlang-process metric families without a capacity and retention review.

Load `prometheus/messaging-alerts.yml` as a Prometheus rule file. Route:

- `owner=service-owner` by the endpoint/service ownership catalogue;
- `owner=platform` to the RabbitMQ platform team.

The rules separate:

- sustained immediate retry invocations;
- delayed broker deliveries;
- failed invocations by bounded disposition;
- terminal `_error` placement;
- `_skipped` routing state;
- business-queue backlog age and capacity pressure;
- pending bus/consumer outbox roles;
- collector failure and stale data;
- RabbitMQ scrape and connection health.

The checked-in thresholds are safe starting values, not universal SLOs. Capacity tests and production
traffic should drive reviewed threshold changes. Alert changes require the same review as application
reliability changes.

## Grafana

Import `grafana/messaging-reliability-dashboard.json` and bind the `DS_PROMETHEUS` variable to the
environment Prometheus datasource. The dashboard contains separate panels for:

- immediate retry invocation rate;
- broker-backed delayed deliveries;
- failed consumer invocation rate by disposition;
- final `_error` and `_skipped` queue depth;
- p95 consumer invocation duration;
- pending outbox count and oldest age by role;
- outbox collector health and staleness;
- business-queue oldest-message age;
- RabbitMQ connection churn.

Business receive queues do not expire messages through queue TTL. Backlog age and capacity rejection
therefore require operator intervention rather than being hidden by message expiration.

## Deployment ordering

Deploy metric-producing application code, Prometheus rules, and the Grafana dashboard as one
coordinated change. During rollout:

1. verify the application exports the new translated metric names;
2. keep old rules only until all old producers are drained;
3. load the new alert rules before removing obsolete ones;
4. import the corrected dashboard;
5. query each expression against live labels;
6. confirm `_error` queue panels do not use failed-attempt counters;
7. remove obsolete metric names only after no remaining deployment emits them.

## Validation

CI validates:

- Grafana dashboard JSON syntax;
- Prometheus alert-rule syntax and PromQL through `promtool check rules`;
- RabbitMQ scrape configuration through `promtool check config`;
- exact custom counter increments for success, immediate retry, delayed redelivery, exhaustion,
  permanent failure, and skipped routing against real RabbitMQ and PostgreSQL.

A successful syntax check does not prove that every metric is present in an environment. Deployment
verification must query each alert and dashboard expression after the first application and RabbitMQ
scrape and confirm expected labels and units. Follow `docs/runbooks/messaging-operations.md` for
incident response and topology or metric migrations.
