-- holdings_by_token also carries the opt-in flag, so historical and latest holders agree with the account-oriented holdings.
-- Runs once: the column is added (and filled from holdings_by_account) only while it does not exist.

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = current_schema() AND table_name = 'holdings_by_token' AND column_name = 'opted_in'
  ) THEN
    ALTER TABLE holdings_by_token ADD COLUMN opted_in boolean NOT NULL DEFAULT true;
    UPDATE holdings_by_token t
    SET opted_in = h.opted_in
    FROM holdings_by_account h
    WHERE h.account = t.account AND h.token_kind = t.token_kind AND h.token_id = t.token_id AND h.opted_in <> t.opted_in;
  END IF;
END
$$;

-- Databases created before the table existed: fill it once from the account-oriented table.
INSERT INTO holdings_by_token (token_kind, token_id, account, amount, last_changed_round, opted_in)
SELECT h.token_kind, h.token_id, h.account, h.amount, h.last_changed_round, h.opted_in
FROM holdings_by_account h
WHERE NOT EXISTS (SELECT 1 FROM holdings_by_token);
