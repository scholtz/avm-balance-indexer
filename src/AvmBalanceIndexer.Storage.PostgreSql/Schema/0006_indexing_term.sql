-- Which build of the indexer wrote which rounds: one row per writer term (ADR 0021).

CREATE TABLE IF NOT EXISTS indexing_term (
  from_round bigint NOT NULL,
  version text NOT NULL,
  started_at timestamptz NOT NULL,
  PRIMARY KEY (from_round, version)
);
