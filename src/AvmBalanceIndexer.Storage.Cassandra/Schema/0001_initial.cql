-- Cassandra schema for one chain keyspace. Applied idempotently by CassandraSchemaManager.
-- Amounts are canonical decimal text, computed with checked BigInteger arithmetic in .NET.
-- Keyspace creation (replication) is deployment-specific and handled by the operator / Helm.

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

CREATE TABLE IF NOT EXISTS holdings_by_account (
  account blob,
  token_kind text,
  token_id bigint,
  amount text,
  opted_in boolean,
  last_changed_round bigint,
  symbol text,
  decimals smallint,
  standard text,
  PRIMARY KEY ((account), token_kind, token_id)
);

CREATE TABLE IF NOT EXISTS effects_by_account_token_bucket (
  account blob,
  token_kind text,
  token_id bigint,
  bucket int,
  round bigint,
  intra_round_order int,
  transaction_id text,
  inner_path list<int>,
  effect_type text,
  delta text,
  balance_after text,
  counterparty blob,
  PRIMARY KEY ((account, token_kind, token_id, bucket), round, intra_round_order, transaction_id)
) WITH CLUSTERING ORDER BY (round DESC, intra_round_order DESC, transaction_id ASC);

CREATE TABLE IF NOT EXISTS effect_buckets_by_account_token (
  account blob,
  token_kind text,
  token_id bigint,
  bucket int,
  first_round bigint,
  last_round bigint,
  PRIMARY KEY ((account, token_kind, token_id), bucket)
) WITH CLUSTERING ORDER BY (bucket DESC);

CREATE TABLE IF NOT EXISTS effects_by_token_bucket (
  token_kind text,
  token_id bigint,
  bucket int,
  shard smallint,
  round bigint,
  intra_round_order int,
  account blob,
  transaction_id text,
  effect_type text,
  delta text,
  PRIMARY KEY ((token_kind, token_id, bucket, shard), round, intra_round_order, account, transaction_id)
) WITH CLUSTERING ORDER BY (round DESC, intra_round_order DESC);

CREATE TABLE IF NOT EXISTS accounts_touched_by_token (
  token_kind text,
  token_id bigint,
  shard smallint,
  account blob,
  first_round bigint,
  last_round bigint,
  PRIMARY KEY ((token_kind, token_id, shard), account)
);
