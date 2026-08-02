# Messaging operations runbook

This runbook applies to services registered through
`AddRabbitMqWithPostgresOutbox<TDbContext>`. Every action requires the service owner or designated
incident commander. Record message counts, queue names, deployment versions, timestamps, and every
operator command in the incident or change record.

Never copy payloads into chat, tickets, metrics, or unrestricted logs. Use access-controlled broker
tooling and approved redaction when payload inspection is required.

## Investigating an `_error` queue

1. Stop automated or manual replay. Record queue depth and oldest-message age.
2. Identify the owning endpoint and deployed consumer version.
3. Inspect MassTransit fault headers, exception type, retry/redelivery counts, MessageId,
   CorrelationId, causation, and message identity. Do not use exception messages as metric labels.
4. Classify the root cause: permanent payload/domain failure, unsupported contract, deterministic
   defect, exhausted transient dependency failure, cancellation/shutdown, or infrastructure defect.
5. Verify database state and external side effects before deciding whether the operation is safe to
   repeat.
6. Deploy remediation and prove it with a representative non-production payload or bounded canary.
7. Replay a small bounded batch while observing database state, produced messages, duplicate
   suppression, `_error`, `_skipped`, retry, and redelivery metrics.
8. Stop immediately if new failures or duplicate side effects appear.
9. Complete the queue only after every message has an explicit disposition: replayed, superseded,
   manually reconciled, or retained under an approved evidence exception.

## Investigating a `_skipped` queue

1. Record queue depth, oldest age, source exchange, routing key, and message identity.
2. Confirm whether any consumer on the endpoint accepts that exact MassTransit message identity.
3. Compare producer and consumer deployment ordering, contract namespace/type, serializer settings,
   endpoint bindings, and recent endpoint renames.
4. Treat malformed content as a contract/serialization incident, not a retryable consumer failure.
5. Correct topology or deploy a compatible consumer before replay.
6. Replay a bounded canary and verify it is consumed rather than moved to `_skipped` again.

## Replaying parked, error, or skipped messages

The repository does not currently implement business expiration or a parking queue. If an
environment has a separately approved parking topology, apply the same controls below.

1. Create an incident/change record and name the replay owner.
2. Confirm root-cause remediation and contract compatibility.
3. Preserve MessageId, CorrelationId, causation, trace headers, content type, and message identity.
4. Confirm inbox duplicate window and all external idempotency keys.
5. Select a bounded batch and rate below downstream capacity.
6. Record pre-replay database, outbox, source, destination, error, and skipped state.
7. Replay a single canary, then a small batch, then increase only while all signals remain healthy.
8. Define a stop threshold for attempts, latency, errors, skipped messages, and downstream failures.
9. Never create new identifiers solely to bypass duplicate suppression.

## Handling an outbox backlog

1. Check `messaging.outbox.collector.healthy` and last-success age before trusting backlog values.
2. Split the signal by DbContext and `bus` or `consumer` role.
3. Check RabbitMQ connectivity, bus health, publisher confirms/rejections, queue capacity, PostgreSQL
   locks, connection saturation, migration state, query timeout, and service logs.
4. Query pending `OutboxMessage` count and oldest `SentTime`; do not count delivered `OutboxState` or
   completed `InboxState` as pending.
5. Confirm the partial indexes are present and valid in `pg_index`.
6. Inspect `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` for the exact role queries in a safe environment.
7. Restore the failed dependency or capacity; do not delete pending rows to clear an alert.
8. Observe automatic drain, produced messages, consumer state, and duplicate protection.
9. Escalate if oldest age continues increasing after dependency recovery.

## Handling RabbitMQ unavailability

1. Confirm broker cluster, delayed-message plugin, disk/memory alarms, network, DNS, TLS, credentials,
   permissions, and connection churn.
2. Expect affected instances to be unready. Liveness must remain healthy unless the process itself is
   unhealthy.
3. Do not restart-loop healthy processes as a substitute for broker recovery.
4. Database transactions may continue to commit bus-outbox messages if the application workflow is
   still accepting work; assess backlog capacity before allowing prolonged operation.
5. Restore broker availability and verify bus reconnect, queue topology, outbox drain, publisher
   rejection metrics, and consumer recovery.
6. Capture broker and service logs before destroying failed infrastructure.

## Handling PostgreSQL unavailability

1. Confirm database cluster, DNS, TLS, credentials, connection pool, failover, locks, and storage.
2. Expect readiness and collector health to fail; last-known backlog values remain stale and must not
   be read as zero.
3. Do not delete outbox, inbox, or application rows during recovery.
4. Restore the database and verify migrations, schema compatibility, collector recovery, consumer
   transactions, and outbox drain.
5. For a connection terminated during a transaction, verify whether the transaction committed before
   retrying application behavior. Use idempotency and database state, not the client exception alone.

## Performing an endpoint rename

1. Treat the change as broker topology migration and create a change record.
2. Inventory old queue depth, consumers, producers, bindings, arguments, error/skipped queues, and
   retention.
3. Register the new stable endpoint name and explicit policy without removing the old endpoint.
4. Deploy consumers capable of processing the new route before producers publish to it.
5. Choose one controlled strategy: stop old publication and drain the old queue, or temporarily
   dual-publish only when deduplication and side effects are proven safe.
6. Keep rollback capable of restoring old publication while the old topology still exists.
7. Remove the old consumer only after its ready and unacknowledged counts are zero and no delayed
   messages can still target it.
8. Remove obsolete exchanges, bindings, queues, policies, dashboards, and alerts through an explicit
   platform change. Application startup does not delete obsolete topology.

## Deploying a breaking contract version

1. Create a distinct message type/namespace identity such as `.V2`; do not mutate V1 incompatibly.
2. Add historical payload tests for every supported version.
3. Deploy V2-capable consumers before enabling V2 producers.
4. During coexistence, use an explicit adapter or dual-publication decision with idempotency and
   ownership. Do not infer version only from a mutable integer payload property.
5. Monitor V1 and V2 queues, error/skipped placement, and consumer adoption.
6. Stop V1 production only after all receivers accept V2 and rollback is understood.
7. Drain retained V1 messages before removing V1 consumers or topology.

## Responding to queue-capacity or publisher-rejection alerts

1. Confirm which business queue reached count or byte capacity and whether publishers are receiving
   `reject-publish` failures.
2. Record queue depth, oldest age, ingress rate, delivery rate, consumer count, concurrency, prefetch,
   retry/redelivery activity, and downstream health.
3. Restore consumer or dependency capacity before increasing queue limits.
4. Do not add receive TTL to shed backlog; that silently discards business messages.
5. Increase limits only after disk, quorum replication, recovery time, and incident blast radius are
   capacity-reviewed.
6. If ingestion must be reduced, apply upstream admission control or a documented rate limit that
   returns visible failure to the caller.

## Deploying the outbox monitoring index migration

The migration builds two partial indexes with `CREATE INDEX CONCURRENTLY` and therefore runs outside
a transaction.

1. Run the service migrator before API replicas are rolled out.
2. Monitor `pg_stat_progress_create_index`, database I/O, and long-lived transactions.
3. On failure, inspect `pg_index.indisvalid` for both named indexes.
4. Drop an invalid index concurrently or use the approved `REINDEX INDEX CONCURRENTLY` recovery.
5. Do not mark the migration applied while an index is invalid or absent.
6. After success, run `ANALYZE "OutboxMessage"` when required and verify query plans use an expected
   index/index-only path under representative backlog.

## Shutdown and deployment drain

1. Remove the instance from load-balancer traffic before process termination.
2. Ensure Kubernetes termination grace exceeds MassTransit stop timeout plus load-balancer margin.
3. Observe active consumers and allow completion within `ConsumerStopTimeout`.
4. If the process exits before acknowledgement, verify RabbitMQ returns the unacknowledged message
   for redelivery.
5. After rollout, verify readiness, bus state, collector health, queue depth, retries, redelivery, and
   error/skipped queues before proceeding to the next batch.
