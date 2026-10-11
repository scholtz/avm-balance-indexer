# ADR 0011: Metrics are exported over OTLP

- Status: accepted
- Date: 2026-10-11
- Issue: #38

## Context

The worker publishes instruments on a `System.Diagnostics.Metrics` meter (ADR 0006) and ASP.NET Core publishes request metrics
on its own meters, but nothing left the process. Operators need indexed round, lag, status age, leadership and request latency in
their monitoring system.

## Decision

- `AddAvmTelemetry` (Hosting) registers OpenTelemetry metrics for the API (ASP.NET Core meters, runtime metrics) and the worker
  (its meter, runtime metrics) with the **OTLP exporter**. It is off unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set or
  `Telemetry:Enabled` is true, so tests and local runs start nothing. Protocol, headers and interval use the standard `OTEL_*`
  environment variables; the chain is a resource attribute (`avm.chain`).
- OTLP instead of a Prometheus scrape endpoint: the Prometheus exporter packages are still pre-release, OTLP is stable, every
  collector accepts it and Prometheus itself has an OTLP receiver. A scrape endpoint can be added later without changing the meters.
- Indexed round, target round, lag and the age of the last status update are sampled from the database every 15 s on every
  worker replica (`StatusSampler`), not computed in the hot path. A rising `avm.indexer.status_age_seconds` or `lag_rounds` is the
  signal to alert on; `avm.worker.is_leader` summed over replicas should be 1.
- The Helm chart sets the endpoint and protocol from `telemetry.*`.

## Consequences

- Alerts and dashboards are built by the operator; no dashboard ships with the chart.
- The OTLP exporter retries and drops on failure, so an unreachable collector costs a little memory and no availability.
