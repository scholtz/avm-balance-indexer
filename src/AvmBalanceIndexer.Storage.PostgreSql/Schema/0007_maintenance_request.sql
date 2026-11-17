-- Maintenance requests that were carried out (the PostgreSQL rewind of ADR 0021), so a request id runs once even when it stays in the configuration.

CREATE TABLE IF NOT EXISTS maintenance_request (
  request_id text PRIMARY KEY,
  kind text NOT NULL,
  round bigint NOT NULL,
  executed_at timestamptz NOT NULL
);
