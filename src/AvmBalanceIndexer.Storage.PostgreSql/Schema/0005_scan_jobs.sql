-- Asynchronous holders-at-round scans: the job queue and the result rows. Jobs expire (expires_at) and are deleted with their results.

CREATE TABLE IF NOT EXISTS scan_job (
  id uuid PRIMARY KEY,
  token_kind text COLLATE "C" NOT NULL,
  token_id bigint NOT NULL,
  round bigint NOT NULL,
  include_zero boolean NOT NULL,
  state text NOT NULL,
  owner text,
  holders_found bigint NOT NULL DEFAULT 0,
  cursor text,
  error text,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL,
  expires_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS scan_job_queue ON scan_job (state, created_at);

CREATE TABLE IF NOT EXISTS scan_result (
  job_id uuid NOT NULL,
  seq bigint NOT NULL,
  account bytea NOT NULL,
  amount numeric(78,0) NOT NULL,
  opted_in boolean NOT NULL,
  last_changed_round bigint NOT NULL,
  PRIMARY KEY (job_id, seq)
);
