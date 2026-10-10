-- PostgreSQL schema for one chain database. Applied idempotently by PostgreSqlSchemaManager.
-- Amounts use numeric(20,0) so uint64 values never overflow signed bigint.

CREATE TABLE IF NOT EXISTS chain_status (
  chain text PRIMARY KEY,
  indexed_round bigint NOT NULL,
  indexed_block_hash bytea NOT NULL,
  target_round bigint,
  status text NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS token (
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  creator bytea,
  decimals smallint,
  symbol text,
  name text,
  standard text,
  standard_verified boolean NOT NULL DEFAULT false,
  controlling_app_id bigint,
  created_round bigint,
  deleted_round bigint,
  metadata_json jsonb,
  PRIMARY KEY (token_kind, token_id)
);

CREATE TABLE IF NOT EXISTS holdings_by_account (
  account bytea NOT NULL,
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  amount numeric(20,0) NOT NULL,
  opted_in boolean NOT NULL,
  last_changed_round bigint NOT NULL,
  PRIMARY KEY (account, token_kind, token_id)
);

-- Hash-partitioned by account; partitions are created by the schema manager
-- using Storage:PostgreSql:AccountHashPartitions.
CREATE TABLE IF NOT EXISTS effects_by_account_token (
  account bytea NOT NULL,
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  round bigint NOT NULL,
  intra_round_order integer NOT NULL,
  transaction_id text NOT NULL,
  inner_path integer[] NOT NULL DEFAULT '{}',
  effect_type text NOT NULL,
  delta numeric(20,0) NOT NULL,
  balance_after numeric(20,0) NOT NULL,
  counterparty bytea,
  PRIMARY KEY (account, token_kind, token_id, round, intra_round_order, transaction_id)
) PARTITION BY HASH (account);

CREATE INDEX IF NOT EXISTS effects_account_token_round_idx
  ON effects_by_account_token
  (account, token_kind, token_id, round DESC, intra_round_order DESC)
  INCLUDE (balance_after, delta, transaction_id);

-- Duplicated token-oriented view, range-partitioned by bounded round bucket.
CREATE TABLE IF NOT EXISTS effects_by_token_bucket (
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  bucket integer NOT NULL,
  round bigint NOT NULL,
  intra_round_order integer NOT NULL,
  account bytea NOT NULL,
  transaction_id text NOT NULL,
  effect_type text NOT NULL,
  delta numeric(20,0) NOT NULL,
  PRIMARY KEY (token_kind, token_id, bucket, round, intra_round_order, account, transaction_id)
) PARTITION BY RANGE (bucket);

CREATE INDEX IF NOT EXISTS effects_token_bucket_round_idx
  ON effects_by_token_bucket (token_kind, token_id, bucket, round, intra_round_order);

CREATE TABLE IF NOT EXISTS accounts_touched_by_token (
  token_kind text NOT NULL,
  token_id bigint NOT NULL,
  account bytea NOT NULL,
  first_round bigint NOT NULL,
  last_round bigint NOT NULL,
  PRIMARY KEY (token_kind, token_id, account)
);
