-- Stops of the ledger audit (ADR 0021): a round at which the database disagreed with the ledger. Kept so that a restart does not index past it.
-- Rows are written only with lightweight transactions (IF NOT EXISTS, IF EXISTS), never with plain writes.

CREATE TABLE IF NOT EXISTS audit_stop (
  chain text,
  round bigint,
  reason text,
  published_round bigint,
  recorded_at timestamp,
  acknowledged boolean,
  PRIMARY KEY ((chain), round)
);
