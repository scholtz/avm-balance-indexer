-- PostgreSQL schema for one chain database. Applied idempotently by PostgreSqlChainStorage.EnsureSchemaAsync.
-- Amounts started as numeric(20,0) so uint64 values never overflow a signed bigint; 0003_wide_amounts.sql widens them to uint256.
--
-- This is the first released schema, so there is nothing to migrate from. Later changes need numbered migration scripts
-- (0002_*.sql, ...) because every statement here is CREATE ... IF NOT EXISTS and would not alter existing tables.
--
-- Deviations from the reference design in PRODUCT-DESCRIPTION.md (see docs/adr/0002):
--   * effect keys end in (round, intra_round_order, ordinal) so several effects of one transaction on the same
--     account cannot collide; transaction_id is a plain column.
--   * no secondary DESC index on effects_by_account_token: the primary key b-tree is scanned backwards for the
--     "latest effect at or before round R" lookup, which avoids doubling write cost on the largest table.

CREATE TABLE IF NOT EXISTS chain_status (
  chain text PRIMARY KEY,
  indexed_round bigint NOT NULL,
  indexed_block_hash bytea NOT NULL,
  target_round bigint,
  status text NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS token (
  token_kind text COLLATE "C" NOT NULL,
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
  token_kind text COLLATE "C" NOT NULL,
  token_id bigint NOT NULL,
  amount numeric(20,0) NOT NULL,
  opted_in boolean NOT NULL,
  last_changed_round bigint NOT NULL,
  PRIMARY KEY (account, token_kind, token_id)
);

-- Hash-partitioned by account. The fixed set of partitions is created by the schema manager
-- using Storage:PostgreSql:AccountHashPartitions.
CREATE TABLE IF NOT EXISTS effects_by_account_token (
  account bytea NOT NULL,
  token_kind text COLLATE "C" NOT NULL,
  token_id bigint NOT NULL,
  round bigint NOT NULL,
  intra_round_order integer NOT NULL,
  ordinal integer NOT NULL,
  transaction_id text NOT NULL,
  inner_path integer[] NOT NULL DEFAULT '{}',
  effect_type text NOT NULL,
  delta numeric(20,0) NOT NULL,
  balance_after numeric(20,0) NOT NULL,
  counterparty bytea,
  PRIMARY KEY (account, token_kind, token_id, round, intra_round_order, ordinal)
) PARTITION BY HASH (account);

-- Duplicated token-oriented view, range-partitioned by bounded round bucket (see RoundBuckets).
-- Bucket partitions are created ahead of ingestion by the round writer.
CREATE TABLE IF NOT EXISTS effects_by_token_bucket (
  token_kind text COLLATE "C" NOT NULL,
  token_id bigint NOT NULL,
  bucket integer NOT NULL,
  round bigint NOT NULL,
  intra_round_order integer NOT NULL,
  ordinal integer NOT NULL,
  account bytea NOT NULL,
  transaction_id text NOT NULL,
  effect_type text NOT NULL,
  delta numeric(20,0) NOT NULL,
  PRIMARY KEY (token_kind, token_id, bucket, round, intra_round_order, ordinal)
) PARTITION BY RANGE (bucket);

CREATE TABLE IF NOT EXISTS accounts_touched_by_token (
  token_kind text COLLATE "C" NOT NULL,
  token_id bigint NOT NULL,
  account bytea NOT NULL,
  first_round bigint NOT NULL,
  last_round bigint NOT NULL,
  PRIMARY KEY (token_kind, token_id, account)
);
