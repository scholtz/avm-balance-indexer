-- Stops of the ledger audit (ADR 0021): a round at which the database disagreed with the ledger. Kept so that a restart does not index past it.

CREATE TABLE IF NOT EXISTS audit_stop (
  round bigint PRIMARY KEY,
  reason text NOT NULL,
  published_round bigint NOT NULL,
  recorded_at timestamptz NOT NULL,
  acknowledged boolean NOT NULL DEFAULT false
);
