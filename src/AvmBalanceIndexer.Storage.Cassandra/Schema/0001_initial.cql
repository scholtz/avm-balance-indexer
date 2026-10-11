-- Cassandra schema for one chain keyspace. Applied idempotently by CassandraChainStorage.EnsureSchemaAsync, one
-- statement per semicolon. Keep comments on their own lines and never put a semicolon inside a statement or comment.
-- Amounts are canonical decimal text, computed with checked BigInteger arithmetic in .NET.
-- The keyspace itself is created by the schema manager when Storage:Cassandra:CreateKeyspace is true; production
-- replication settings may instead be applied by the operator / Helm before the first start.
--
-- Deviations from the reference design in PRODUCT-DESCRIPTION.md (see docs/adr/0002):
--   * effect keys end in (round, intra_round_order, ordinal) so several effects of one transaction on the same
--     account cannot collide; transaction_id is a plain column.
--   * holdings_by_account and accounts_touched_by_token carry no columns that nothing reads.

CREATE TABLE IF NOT EXISTS chain_status (
  chain text PRIMARY KEY,
  indexed_round bigint,
  indexed_block_hash blob,
  target_round bigint,
  status text,
  updated_at timestamp
);

CREATE TABLE IF NOT EXISTS token_by_id (
  token_kind text,
  token_id bigint,
  creator blob,
  decimals smallint,
  symbol text,
  name text,
  standard text,
  standard_verified boolean,
  controlling_app_id bigint,
  created_round bigint,
  deleted_round bigint,
  metadata_json text,
  PRIMARY KEY ((token_kind, token_id))
);

-- Current holdings. Written with USING TIMESTAMP = round so a stale writer can never overwrite a newer round.
CREATE TABLE IF NOT EXISTS holdings_by_account (
  account blob,
  token_kind text,
  token_id bigint,
  amount text,
  opted_in boolean,
  last_changed_round bigint,
  PRIMARY KEY ((account), token_kind, token_id)
);

-- Account history and the source of "latest effect at or before round R". Newest first.
CREATE TABLE IF NOT EXISTS effects_by_account_token_bucket (
  account blob,
  token_kind text,
  token_id bigint,
  bucket int,
  round bigint,
  intra_round_order int,
  ordinal int,
  transaction_id text,
  inner_path list<int>,
  effect_type text,
  delta text,
  balance_after text,
  counterparty blob,
  PRIMARY KEY ((account, token_kind, token_id, bucket), round, intra_round_order, ordinal)
) WITH CLUSTERING ORDER BY (round DESC, intra_round_order DESC, ordinal DESC);

-- Which round buckets hold effects of an account/token, so a balance lookup skips empty buckets.
CREATE TABLE IF NOT EXISTS effect_buckets_by_account_token (
  account blob,
  token_kind text,
  token_id bigint,
  bucket int,
  PRIMARY KEY ((account, token_kind, token_id), bucket)
) WITH CLUSTERING ORDER BY (bucket DESC);

-- Token oriented duplicate of the effects, sharded by account so one token never concentrates in one partition.
CREATE TABLE IF NOT EXISTS effects_by_token_bucket (
  token_kind text,
  token_id bigint,
  bucket int,
  shard smallint,
  round bigint,
  intra_round_order int,
  ordinal int,
  account blob,
  transaction_id text,
  effect_type text,
  delta text,
  PRIMARY KEY ((token_kind, token_id, bucket, shard), round, intra_round_order, ordinal)
) WITH CLUSTERING ORDER BY (round DESC, intra_round_order DESC, ordinal DESC);

-- Every account that ever changed a token, with the round of its first effect. Sharded by account.
CREATE TABLE IF NOT EXISTS accounts_touched_by_token (
  token_kind text,
  token_id bigint,
  shard smallint,
  account blob,
  first_round bigint,
  PRIMARY KEY ((token_kind, token_id, shard), account)
);
