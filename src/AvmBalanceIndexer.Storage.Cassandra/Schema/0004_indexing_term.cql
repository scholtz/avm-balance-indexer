-- Which build of the indexer wrote which rounds: one row per writer term (ADR 0021). Written only with IF NOT EXISTS (the first time of a
-- term is kept), never with a plain write.

CREATE TABLE IF NOT EXISTS indexing_term (
  chain text,
  from_round bigint,
  version text,
  started_at timestamp,
  PRIMARY KEY ((chain), from_round, version)
) WITH CLUSTERING ORDER BY (from_round DESC, version ASC);
