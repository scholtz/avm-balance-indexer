-- Asynchronous holders-at-round scans. A job is one row changed only through conditional updates; scan_job_index lists the jobs of
-- the chain so a worker can find pending ones; results are numbered rows in bounded partitions. Expired jobs are deleted by the
-- workers (DeleteExpiredAsync), not by TTL, so a job and its results always disappear together.

CREATE TABLE IF NOT EXISTS scan_job (
  id uuid PRIMARY KEY,
  token_kind text,
  token_id bigint,
  round bigint,
  include_zero boolean,
  state text,
  owner text,
  holders_found bigint,
  cursor text,
  error text,
  created_at timestamp,
  updated_at timestamp,
  expires_at timestamp
);

CREATE TABLE IF NOT EXISTS scan_job_index (
  chain text,
  id uuid,
  created_at timestamp,
  PRIMARY KEY ((chain), id)
);

CREATE TABLE IF NOT EXISTS scan_result (
  job_id uuid,
  bucket int,
  seq bigint,
  account blob,
  amount text,
  opted_in boolean,
  last_changed_round bigint,
  PRIMARY KEY ((job_id, bucket), seq)
);
