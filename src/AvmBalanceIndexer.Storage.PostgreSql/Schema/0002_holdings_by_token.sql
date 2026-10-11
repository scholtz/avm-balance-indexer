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

-- Databases created before this table existed: fill it once from the account-oriented table.
INSERT INTO holdings_by_token (token_kind, token_id, account, amount, last_changed_round)
SELECT h.token_kind, h.token_id, h.account, h.amount, h.last_changed_round
FROM holdings_by_account h
WHERE NOT EXISTS (SELECT 1 FROM holdings_by_token);
