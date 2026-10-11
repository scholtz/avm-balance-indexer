# ADR 0006: Writer lease, failover and worker health

- Status: accepted
- Date: 2026-10-11
- Issue: #17

## Context

Exactly one ordered writer may index a chain. Until now that was an operator rule (one replica, `Recreate` rollouts). A crashed
or evicted pod therefore meant downtime until Kubernetes rescheduled it, and a second replica started by mistake raced the first.

## Decision

- `IWriterLeaseProvider.TryAcquireAsync` hands out an `IWriterLease` or null (never blocks); `IWriterLease.Lost` is cancelled
  when the lease can no longer be trusted. Both providers implement it next to the storage contract, with the same shared test.
- **PostgreSQL:** a session-level advisory lock (`pg_try_advisory_lock`, key seeded differently from the transaction lock inside
  `WriteRoundAsync`) on a dedicated connection that stays open. The server releases it when the session dies, so failover needs no
  timer. The lease session sets short TCP keepalives (`tcp_keepalives_idle` 15 s, interval 5 s, count 3) so the server drops a
  silently dead client within about 30 s instead of the OS default of hours. A heartbeat (`SELECT 1`, default every 5 s) detects
  a session that cannot answer and reports the lease lost.
- **Cassandra:** a `writer_lease` row with TTL (default 30 s) created with `IF NOT EXISTS` and renewed every TTL/3 with
  `IF owner = me`. A failed condition means someone else owns it; renewal errors are retried until 80% of the TTL has passed
  without success, then the lease is reported lost before the row can expire under the owner (a timer pushed back by every
  confirmed renewal, measured from when the request was sent, so a hung renewal cannot delay it). Lease statements use `SERIAL`
  consistency so the lease is exclusive across datacenters.
- **Worker:** a replica loops: acquire the lease (otherwise stand by and retry every `Indexer:LeaseRetryInterval`), index with a
  token linked to `Lost`, and on loss stop at once and go back to waiting. Database trouble while asking for the lease is a wait,
  not a crash; every replica applies the schema before asking, because the lease needs its table. A commit that fails against the
  new writer's cursor after the lease was lost is treated as the loss, not as a data problem. A fatal indexing error still stops the process and releases the lease.
- **The lease is an optimisation, not the safety net.** A stale writer that keeps going after losing the lease is still stopped
  by the independent protections: the PostgreSQL writer lock and cursor check in the commit transaction, and the Cassandra
  cursor compare-and-set (ADR 0003).
- **Health and metrics:** the worker serves `/healthz/live` (the process answers) and `/healthz/ready` (the replica knows whether
  it leads or stands by; a standby is ready) on `Worker:HealthPort` through a small `HttpListener`, because the worker is not a web
  application. Metrics are `System.Diagnostics.Metrics` instruments on the meter `AvmBalanceIndexer.Worker`
  (`avm.worker.is_leader`, `avm.worker.leases_acquired`, `avm.worker.leases_lost`, `avm.worker.transient_failures`, `avm.worker.rounds_committed`); any OpenTelemetry or Prometheus
  exporter can subscribe. The Helm chart runs two worker replicas with rolling updates and probes.

## Consequences

- Failover time is bounded by the Cassandra TTL, or by how fast PostgreSQL notices a dead TCP session.
- The heartbeat connection is one extra pooled connection per replica for the lifetime of the lease.
- No exporter is bundled; wiring metrics to a backend is deployment work.
