-- Current holdings by token, so the holders of a token at the indexed round are one ordered index range scan.
-- Maintained by the round writer in the same transaction as holdings_by_account.

CREATE TABLE IF NOT EXISTS holdings_by_token (
  token_kind text COLLATE "C" NOT NULL,
  token_id bigint NOT NULL,
  account bytea NOT NULL,
  amount numeric(20,0) NOT NULL,
  last_changed_round bigint NOT NULL,
  PRIMARY KEY (token_kind, token_id, account)
);

-- The backfill of databases that predate this table is in 0003, once the opt-in column exists.
