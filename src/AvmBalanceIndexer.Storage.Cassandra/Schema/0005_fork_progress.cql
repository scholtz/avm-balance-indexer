-- Partial rebuild by copying an earlier keyspace up to a round (ADR 0021, issue #178). One row per finished range of a copy stage, so a
-- restarted copy skips them, and one row (stage 'request', range 0) that binds the keyspace to its request. The request row is written only
-- with IF NOT EXISTS, the range rows only with plain writes: no row is ever written both ways.

CREATE TABLE IF NOT EXISTS fork_progress (
  stage text,
  range int,
  source text,
  round bigint,
  PRIMARY KEY ((stage), range)
);
